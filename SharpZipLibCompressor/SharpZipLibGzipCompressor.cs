﻿//  Copyright 2011 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using SerializerBase;
using System.IO;

namespace SharpZipLibCompressor
{
    /// <summary>
    /// Compresor using Gzip compression from SharpZipLib http://www.icsharpcode.net/opensource/sharpziplib/
    /// </summary>
    public class SharpZipLibGzipCompressor : ICompress
    {
        static SharpZipLibGzipCompressor instance;
        static object locker = new object();

        public static SharpZipLibGzipCompressor Instance
        {
            get
            {
                lock (locker)
                {
                    if (instance == null)
                        instance = new SharpZipLibGzipCompressor();
                }

                return instance;
            }
        }

        private SharpZipLibGzipCompressor() { }

        /// <summary>
        /// Compresses data in inStream to a byte array appending uncompressed data size
        /// </summary>
        /// <param name="inStream">Stream contaiing data to compress</param>
        /// <returns>Compressed data appended with uncompressed data size</returns>
        public byte[] CompressDataStream(Stream inStream)
        {
            using (MemoryStream realOutStream = new MemoryStream())
            {
                using (GZipOutputStream outStream = new GZipOutputStream(realOutStream))
                {
                    outStream.IsStreamOwner = false;
                    inStream.CopyTo(outStream);
                }

                ulong nBytes = (ulong)inStream.Length;
                realOutStream.Write(BitConverter.GetBytes(nBytes), 0, 8);

                return realOutStream.ToArray();
            }            
        }

        /// <summary>
        /// Decompresses data from inBytes into outStream
        /// </summary>
        /// <param name="inBytes">Compressed data from CompressDataStream</param>
        /// <param name="outputStream">Stream to output uncompressed data to</param>
        public void DecompressToStream(byte[] inBytes, Stream outputStream)
        {
            using (MemoryStream memIn = new MemoryStream(inBytes, 0, inBytes.Length - 8, false))
            {
                using (GZipInputStream zip = new GZipInputStream(memIn))
                {
                    zip.CopyTo(outputStream);
                }

                outputStream.Seek(0, 0);
            }
        }
    }
}