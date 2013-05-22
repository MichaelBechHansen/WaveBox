﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WaveBox.Model;
using WaveBox.Static;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using WaveBox.OperationQueue;
using TagLib;

namespace WaveBox.FolderScanning
{
	public class FolderScanOperation : AbstractOperation
	{
		private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public override string OperationType { get { return String.Format ("FolderScanOperation: {0}", FolderPath); } }

		private string folderPath;
		public string FolderPath { get { return folderPath; } }

		int testNumberOfFoldersInserted = 0;
		Stopwatch testFolderObjCreateTime = new Stopwatch();
		Stopwatch testGetDirectoriesTime = new Stopwatch();
		Stopwatch testMediaItemNeedsUpdatingTime = new Stopwatch();
		Stopwatch testIsExtensionValidTime = new Stopwatch();

		public FolderScanOperation(string path, int delayMilliSeconds) : base(delayMilliSeconds)
		{
			folderPath = path;
		}

		public override void Start()
		{
			ProcessFolder(FolderPath);

			if (logger.IsInfoEnabled) logger.Info("---------------- FOLDER SCAN ----------------");
			if (logger.IsInfoEnabled) logger.Info("folders inserted: " + testNumberOfFoldersInserted);
			if (logger.IsInfoEnabled) logger.Info("folder object create time: " + testFolderObjCreateTime.ElapsedMilliseconds + "ms");
			if (logger.IsInfoEnabled) logger.Info("get directories time: " + testGetDirectoriesTime.ElapsedMilliseconds + "ms");
			if (logger.IsInfoEnabled) logger.Info("media file needs updating time: " + testMediaItemNeedsUpdatingTime.ElapsedMilliseconds + "ms");
			if (logger.IsInfoEnabled) logger.Info("extension valid check time: " + testIsExtensionValidTime.ElapsedMilliseconds + "ms");
			long total = testFolderObjCreateTime.ElapsedMilliseconds + testGetDirectoriesTime.ElapsedMilliseconds + testMediaItemNeedsUpdatingTime.ElapsedMilliseconds + testIsExtensionValidTime.ElapsedMilliseconds;
			if (logger.IsInfoEnabled) logger.Info("total: " + total + "ms = " + total / 1000 + "s");
			if (logger.IsInfoEnabled) logger.Info("---------------------------------------------");
		}

		public void ProcessFolder(int folderId)
		{
			Folder folder = new Folder.Factory().CreateFolder(folderId);
			ProcessFolder(folder.FolderPath);
		}

		public void ProcessFolder(string folderPath)
		{
			if (isRestart)
			{
				return;
			}

			try
			{
				// if the file is a directory
				if (Directory.Exists(folderPath))
				{
					testFolderObjCreateTime.Start();
					Folder topFolder = new Folder.Factory().CreateFolder(folderPath);
					testFolderObjCreateTime.Stop();
					//if (logger.IsInfoEnabled) logger.Info("scanning " + topFolder.FolderName + "  id: " + topFolder.FolderId);

					if (topFolder.FolderId == null)
					{
						testNumberOfFoldersInserted++;
						topFolder.InsertFolder(false);
					}

					/*
					// Check the folder art
					string artPath = topFolder.ArtPath;
					if (Art.FileNeedsUpdating(artPath, topFolder.FolderId))
					{
						// Find the old art id, if it exists
						int? oldArtId = topFolder.ArtId;
						int? newArtId = new Art(artPath).ArtId;

						if ((object)oldArtId == null)
						{
							// Insert the relationship
							Art.UpdateArtItemRelationship(newArtId, topFolder.FolderId, true);
						}
						else
						{
							Art oldArt = new Art((int)oldArtId);

							// Check if the previous folder art was actually from embedded tag art
							if ((object)oldArt.FilePath == null)
							{
								// This was embedded tag art, so only update the folder's relationship
								Art.UpdateArtItemRelationship(newArtId, topFolder.FolderId, true);
							}
							else
							{
								// Update any existing references, that would include both this folder
								// and any children that were using this art in lieu of embedded art
								Art.UpdateItemsToNewArtId(oldArtId, newArtId);
							}
						}
					}
					*/

					testGetDirectoriesTime.Start();
					string[] directories = Directory.GetDirectories(folderPath);
					testGetDirectoriesTime.Stop();
					foreach (string subfolder in directories)
					{
						if (!subfolder.Contains(".AppleDouble"))
						{
							testFolderObjCreateTime.Start();
							Folder folder = new Folder.Factory().CreateFolder(subfolder);
							testFolderObjCreateTime.Stop();

							// if the folder isn't already in the database, add it.
							if (folder.FolderId == null)
							{
								testNumberOfFoldersInserted++;
								folder.InsertFolder(false);
							}

							ProcessFolder(subfolder);
						}
					}

					Parallel.ForEach(Directory.GetFiles(folderPath), currentFile =>
						{
							ProcessFile(currentFile, topFolder.FolderId);
						});
				}
			}
			catch (FileNotFoundException e)
			{
				logger.Error("\"" + folderPath + "\" : Directory does not exist. " + e);
			}
			catch (DirectoryNotFoundException e)
			{
				logger.Error("\"" + folderPath + "\" : Directory does not exist. " + e);
			}
			catch (IOException e)
			{
				logger.Error("\"" + folderPath + "\" : " + e);
			}
			catch (UnauthorizedAccessException e)
			{
				logger.Error("\"" + folderPath + "\" : Access denied. " + e);
			}
			catch (Exception e)
			{
				logger.Error("\"" + folderPath + "\" : Error checking to see if the file was a directory: " + e);
			}
		}

		public void ProcessFile(string file, int? folderId)
		{
			if (isRestart)
			{
				return;
			}

			try
			{
				ItemType type = Item.ItemTypeForFilePath(file);

				if (type == ItemType.Song || type == ItemType.Video)
				{
					testMediaItemNeedsUpdatingTime.Start();
					bool isNew = true;
					int? itemId = null;
					bool needsUpdating = MediaItem.FileNeedsUpdating(file, folderId, out isNew, out itemId);
					testMediaItemNeedsUpdatingTime.Stop();

					if (needsUpdating)
					{
						if (logger.IsInfoEnabled) logger.Info("File needs updating: " + file);
						
						TagLib.File f = null;
						try
						{
							f = TagLib.File.Create(file);
						}
						catch (TagLib.CorruptFileException e)
						{
							logger.Error(file + " has a corrupt tag and will not be inserted. " + e);
							return;
						}
						catch (Exception e)
						{
							logger.Error("Error processing file " + file + ":	" + e);
						}

						if (f == null)
						{
							// Must be something not supported by TagLib-Sharp
							if (logger.IsInfoEnabled) logger.Info(file + " is not supported by taglib and will not be inserted.");
						}
						else
						{
							if (type == ItemType.Song)
							{
								// It's a song!  Do yo thang.
								if (isNew)
								{
									new Song(file, folderId, f).InsertMediaItem();
								}
								else if (itemId != null)
								{
									var oldSong = new Song((int)itemId);
									var newSong = new Song(file, folderId, f);
									newSong.ItemId = oldSong.ItemId;
									newSong.InsertMediaItem();
								}
							}
							else if (type == ItemType.Video)
							{
								if (isNew)
								{
									new Video(file, folderId, f).InsertMediaItem();
								}
								else if (itemId != null)
								{
									new Video((int)itemId).InsertMediaItem();
								}
							}
						}
					}
				}
				else if (type == ItemType.Art)
				{
					if (Art.FileNeedsUpdating(file, folderId))
					{
						var folder = new Folder.Factory().CreateFolder((int)folderId);

						// Find the old art id, if it exists
						int? oldArtId = folder.ArtId;
						int? newArtId = new Art(file).ArtId;
						
						if ((object)oldArtId == null)
						{
							if (logger.IsInfoEnabled) logger.Info("There was no old art id");

							// Insert the relationship
							Art.UpdateArtItemRelationship(newArtId, folder.FolderId, true);

							// If there was no old art id, there will be no items that have said non-existent art id.
							//Art.UpdateItemsToNewArtId(oldArtId, newArtId);
						}
						else
						{
							if (logger.IsInfoEnabled) logger.Info("There was an old art id");

							Art oldArt = new Art((int)oldArtId);
							
							// Check if the previous folder art was actually from embedded tag art
							if ((object)oldArt.FilePath == null)
							{
								// This was embedded tag art, so only update the folder's relationship
								if (logger.IsInfoEnabled) logger.Info(String.Format("It was embedded art, {0}, newArtId: {1}, folderId: {2}", Art.UpdateArtItemRelationship(newArtId, folder.FolderId, true), newArtId, folder.FolderId));
							}
							else
							{
								// Update any existing references, that would include both this folder
								// and any children that were using this art in lieu of embedded art
								Art.UpdateItemsToNewArtId(oldArtId, newArtId);
							}
						}

						// Add this art to any media items in this folder which have no art.
						var items = folder.ListOfMediaItems();
						
						foreach (MediaItem m in items)
						{
							if(m.ArtId == null)
							{
								if (logger.IsInfoEnabled) logger.Info("Updating art id for item " + m.ItemId + ". (" + (m.ArtId == null ? "null" : m.ArtId.ToString()) + " -> " + newArtId + ")");
								Art.UpdateArtItemRelationship(newArtId, m.ItemId, false);
							}
						}

						if (logger.IsInfoEnabled) logger.Info("Art needs updating: " + folder.ArtPath);
					}
				}
			}
			catch (FileNotFoundException e)
			{
				logger.Error("\"" + file + "\" : File does not exist. " + e);
			}
			catch (DirectoryNotFoundException e)
			{
				logger.Error("\"" + file + "\" : Directory does not exist. " + e);
			}
			catch (IOException e)
			{
				logger.Error("\"" + file + "\" : IO error. " + e);
			}
			catch (UnauthorizedAccessException e)
			{
				logger.Error("\"" + file + "\" : Access denied. " + e);
			}
			catch (Exception e)
			{
				logger.Error("\"" + file + "\" : " + e);
			}
		}
	}
}

