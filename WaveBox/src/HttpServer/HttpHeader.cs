﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace WaveBox.Http
{
	class HttpHeader
	{
		public long ContentLength { get; set; }
		public HttpStatusCode StatusCode { get; set; }
		public string ContentType { get; set; }

		public enum HttpStatusCode
		{
			OK = 0, 
			NOTFOUND = 1,
            PARTIALCONTENT = 2
		}

        public enum HttpContentType
        {
            AUDIOMP3 = 0,
            AUDIOMP4 = 1,
            AUDIOOGG = 2,
            AUDIOWEBM = 3,
            AUDIOWAV = 4,
            VIDEOWEBM = 5,
            VIDEOMP4 = 6,
            VIDEOOGG = 7,
            UNKNOWN = 8
        }

        private string[] HttpContentTypeStrings = { "audio/mpeg", "audio/mp4", "audio/ogg", "audio/webm", "audio/wav", "video/webm", "video/mp4", "video/ogg", "octet-stream" };

        public static HttpContentType ContentTypeForExtension(string extension)
        {
            string ext = extension.ToLower();

            if (ext == ".mp3")
                return HttpContentType.AUDIOMP3;
            else if (ext == ".m4a")
                return HttpContentType.AUDIOMP4;
            else if (ext == ".ogg" || ext == "oga")
                return HttpContentType.AUDIOOGG;
            else if (ext == ".webma")
                return HttpContentType.AUDIOWEBM;
            else if (ext == ".wav")
                return HttpContentType.AUDIOWAV;
            else if (ext == ".mp4" || ext == ".m4v")
                return HttpContentType.VIDEOMP4;
            else if (ext == ".ogv") 
                return HttpContentType.VIDEOOGG;
            else if (ext == ".webm" || ext == ".webmv")
                return HttpContentType.VIDEOWEBM;
            else return HttpContentType.UNKNOWN;
        }

		public HttpHeader()
		{
			StatusCode = HttpStatusCode.OK;
			ContentType = "text/html";
			ContentLength = 0;
		}

		public HttpHeader(HttpStatusCode s, HttpContentType cType, long cLen)
		{
			StatusCode = s;
			ContentType = HttpContentTypeStrings[(int)cType];
			ContentLength = cLen;
		}

		public void WriteHeader(StreamWriter outputStream)
		{
			// write the headers
			outputStream.Write(HeaderStringWithResponseCode(StatusCode) + "\r\n");
			outputStream.Write("Content-Length: " + ContentLength + "\r\n");
			if(ContentType != "")
				outputStream.Write("Content-Type: " + ContentType + "\r\n");

			// write the last newline signifying the end of the headers
			outputStream.Write("\r\n");
		}

		public string HeaderStringWithResponseCode(HttpStatusCode c)
		{
			switch (c)
			{
				case HttpStatusCode.OK: return "HTTP/1.0 200 OK";
				case HttpStatusCode.NOTFOUND: return "HTTP/1.0 404 File not found";
                case HttpStatusCode.PARTIALCONTENT: return "HTTP/1.1 206 Partial content";
				default: return null;
			}
		}
	}
}
