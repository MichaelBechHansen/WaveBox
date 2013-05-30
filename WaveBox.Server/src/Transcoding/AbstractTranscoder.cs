using System;
using System.IO;
using System.Threading;
using WaveBox.Model;
using System.Diagnostics;

namespace WaveBox.Transcoding
{
	public abstract class AbstractTranscoder : ITranscoder
	{
		private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		protected ITranscoderDelegate TranscoderDelegate { get; set; }

		public IMediaItem Item { get; set; }

		public TranscodeState State { get; set; }

		// This is either a TranscodeQuality enum value or if higher, a constant bitrate
		public uint Quality { get; set; }

		public int ReferenceCount { get; set; }

		public bool IsDirect { get; set; }

		public abstract TranscodeType Type { get; }

		public abstract string MimeType { get; }

		public abstract string OutputExtension { get; }

		public abstract uint? EstimatedBitrate { get; }

		public abstract string Codec { get; }

		public abstract string Command { get; }
		
		public abstract string Arguments { get; }

		public uint OffsetSeconds { get; set; }
		
		public uint LengthSeconds { get; set; }
		
		public Thread TranscodeThread { get; set; }
		public Process TranscodeProcess { get; set; }

		public string OutputFilename
		{
			get
			{
				return Item.ItemTypeId + "_" + Item.ItemId + "_" + Type + "_" + Quality + "." + OutputExtension;
			}
		}

		public string OutputPath 
		{
			get 
			{ 
				if (Item != null)
				{
					string path = IsDirect ? "-" : TranscodeManager.TranscodePath + Path.DirectorySeparatorChar + OutputFilename;
					return path;
				}

				return null; 
			} 
		}

		public long? EstimatedOutputSize 
		{ 
			get 
			{
				if (this.State == TranscodeState.Finished)
				{
					if (logger.IsInfoEnabled) logger.Info(this.Item.FileName + " finished transcoding. Reporting actual file length.");
					var transFileInfo = new FileInfo(this.OutputPath);
					return transFileInfo.Length;
				}
				if (Item != null)
				{
					return ((long)Item.Duration * (long)EstimatedBitrate * (long)1024) / 8;
				}
				return null;
			}
		}

		public AbstractTranscoder(IMediaItem item, uint quality, bool isDirect, uint offsetSeconds, uint lengthSeconds)
		{
			State = TranscodeState.None;
			Item = item;
			Quality = quality;
			IsDirect = isDirect;
			OffsetSeconds = offsetSeconds;
			LengthSeconds = lengthSeconds;
		}

		public void CancelTranscode()
		{
			if (TranscodeProcess != null)
			{
				if (logger.IsInfoEnabled) logger.Info("cancelling transcode for " + Item.FileName);

				// Kill the process
				TranscodeProcess.Kill();
				TranscodeProcess = null;

				// Wait for the thread to die
				TranscodeThread.Join();
				TranscodeThread = null;
				
				// Set the state
				State = TranscodeState.Canceled;

				// Inform the delegate
				if ((object)TranscoderDelegate != null)
				{
					TranscoderDelegate.TranscodeFailed(this);
				}
			}
		}

		public void StartTranscode()
		{
			if ((object)TranscodeThread != null || (object)TranscodeProcess != null)
			{
				return;
			}

			if (logger.IsInfoEnabled) logger.Info("starting transcode for " + Item.FileName);

			// Set the state
			State = TranscodeState.Active;

			// Delete any existing file of this name
			if (File.Exists(OutputPath))
			{
			   File.Delete(OutputPath);
			}

			// Start a new thread for the transcode
			TranscodeThread = new Thread(new ThreadStart(Run));
			TranscodeThread.IsBackground = true;
			TranscodeThread.Start();
		}

		public virtual void Run()
		{
			try 
			{
				if (logger.IsInfoEnabled) logger.Info("Forking the process");
				if (logger.IsInfoEnabled) logger.Info(Command + " " + Arguments);

				// Fork the process
				TranscodeProcess = new Process();
				TranscodeProcess.StartInfo.FileName = Command;
				TranscodeProcess.StartInfo.Arguments = Arguments;
				TranscodeProcess.StartInfo.UseShellExecute = false;
				TranscodeProcess.StartInfo.RedirectStandardOutput = true;
				TranscodeProcess.StartInfo.RedirectStandardError = true;
				TranscodeProcess.Start();

				if (logger.IsInfoEnabled) logger.Info("Waiting for process to finish");

				// Block until done
				TranscodeProcess.WaitForExit();

				if (logger.IsInfoEnabled) logger.Info("Process finished");
			}
			catch (Exception e) 
			{
				if (logger.IsInfoEnabled) logger.Info("\t" + "Failed to start transcode process " + e);

				// Set the state
				State = TranscodeState.Failed;

				// Inform the delegate
				if ((object)TranscoderDelegate != null)
				{
					TranscoderDelegate.TranscodeFailed(this);
				}

				return;
			}

			if (TranscodeProcess != null)
			{
				int exitValue = TranscodeProcess.ExitCode;
				if (logger.IsInfoEnabled) logger.Info("exit value " + exitValue);

				if (exitValue == 0)
				{
					State = TranscodeState.Finished;

					if ((object)TranscoderDelegate != null)
					{
						TranscoderDelegate.TranscodeFinished(this);
					}
				}
				else
				{
					State = TranscodeState.Failed;
					
					if ((object)TranscoderDelegate != null)
					{
						TranscoderDelegate.TranscodeFailed(this);
					}
				}
			}
		}

		public override bool Equals(Object obj)
		{
			// If they are the exact same object, return true
			if (Object.ReferenceEquals(this, obj))
			{
				return true;
			}
			else if (IsDirect)
			{
				// If this is a direct transcoder, only use reference equality
				return false;
			}

			// If parameter is null return false.
			if ((object)obj == null)
			{
				return false;
			}

			// If the types don't match exactly, return false
			if (this.GetType() != obj.GetType())
			{
				return false;
			}

			// If parameter cannot be cast to AbstractTranscoder return false.
			AbstractTranscoder op = obj as AbstractTranscoder;
			if ((object)op == null)
			{
				return false;
			}

			// Return true if the fields match:
			return Equals(op);
		}

		public bool Equals(AbstractTranscoder op)
		{
			// If parameter is null return false:
			if ((object)op == null)
			{
				return false;
			}

			// Return true if they match
			return Item.Equals(op.Item) && Type == op.Type && Quality == op.Quality;
		}

		public override int GetHashCode()
		{
			int hash = 13;
			hash = (hash * 7) + (Item == null ? Item.GetHashCode() : 0);
			hash = (hash * 7) + Type.GetHashCode();
			hash = (hash * 7) + Quality.GetHashCode();
			return hash;
		}
	}
}

