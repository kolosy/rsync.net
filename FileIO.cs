/**
 *  Copyright (C) 2006 Alex Pedenko
 * 
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program; if not, write to the Free Software
 *  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */
using System.IO;

namespace NetSync
{

	public class FileIO
	{
		static byte lastByte;
		static int lastSparse;

		public static int SparseEnd(Stream f)
		{
			if (lastSparse != 0) 
			{
				f.Seek(-1, SeekOrigin.Current);
				f.WriteByte(lastByte);				
				return 0;
			}
			lastSparse = 0;
			return 0;
		}


		public static int WriteSparse(Stream f,byte[] buf ,int len)
		{
			int l1=0, l2=0;
			int ret;

			for (l1 = 0; l1 < len && buf[l1] == 0; l1++) {}
			for (l2 = 0; l2 < len-l1 && buf[len-(l2+1)] == 0; l2++) {}

			lastByte = buf[len-1];

			if (l1 == len || l2 > 0)
				lastSparse=1;

			if (l1 > 0) 
				f.Seek(l1, SeekOrigin.Current);


			if (l1 == len)
				return len;

			f.Write(buf, l1, len - (l1+l2));			
			ret = len - (l1+l2);
			if (ret == -1 || ret == 0)
				return ret;
			else if (ret != (len - (l1+l2)))
				return (l1+ret);

			if (l2 > 0)
				f.Seek(l2, SeekOrigin.Current);

			return len;
		} 
		public static int WriteFile(Stream f,byte[] buf ,int off, int len)
		{
			f.Write(buf, off, len);
			return len;			
		}
 
	}

	public class MapFile
	{
		public byte[] p = null;		/* Window pointer			*/
		Stream fd;			/* File Descriptor			*/
		int pSize;		/* Largest window size we allocated	*/
		int pLen;		/* Latest (rounded) window size		*/
		int defWindowSize;	/* Default window size			*/
		public bool status = false;		/* first errno from read errors		*/
		public int fileSize;	/* File size (from stat)		*/
		int pOffset;		/* Window start				*/
		int pFdOffset;	/* offset of cursor in fd ala lseek	*/ 

		public MapFile(Stream fd, int len, int mapSize, int blockSize)
		{
			if (blockSize != 0 && (mapSize % blockSize) != 0)
				mapSize += blockSize - (mapSize % blockSize);
			this.fd = fd;
			this.fileSize = len;
			this.defWindowSize = mapSize;

		}

		public int MapPtr(int offset, int len) //returns offset in p array
		{
			int nread;
			int windowStart, readStart;
			int windowSize, readSize, readOffset;

			if(len == 0)
				return -1;
			
			if (len > (this.fileSize - offset)) 
				len = this.fileSize - offset;

			if (offset >= this.pOffset && offset+len <= this.pOffset + this.pLen) 
				return offset - this.pOffset;

			windowStart = offset;
			windowSize = this.defWindowSize;
			if (windowStart + windowSize > this.fileSize) 
				windowSize = this.fileSize - windowStart;
			if (offset + len > windowStart + windowSize) 
				windowSize = (offset+len) - windowStart;

			if (windowSize > this.pSize) 
			{
				ReallocArray(ref p, windowSize);
				this.pSize = windowSize;
			}

			if (windowStart >= this.pOffset &&
				windowStart < this.pOffset + this.pLen &&
				windowStart + windowSize >= this.pOffset + this.pLen) 
			{
				readStart = this.pOffset + this.pLen;
				readOffset = readStart - windowStart;
				readSize = windowSize - readOffset;
				MemMove(ref this.p, this.p, (this.pLen - readOffset), readOffset);
			} 
			else 
			{
				readStart = windowStart;
				readSize = windowSize;
				readOffset = 0;
			}
			if (readSize <= 0) 
				Log.WriteLine("Warning: unexpected read size of " + readSize + " in MapPtr");
			else 
			{
				if (this.pFdOffset != readStart) 
				{
					if (this.fd.Seek(readStart,SeekOrigin.Begin) != readStart) 
					{						
						MainClass.Exit("Seek failed in MapPtr", null);
					}
					this.pFdOffset = readStart;
				}

				if ((nread=fd.Read(this.p,readOffset,readSize)) != readSize) 
				{
					if (nread < 0) 
					{
						nread = 0;
						status = true;
					}
					FillMem(ref this.p, readOffset+nread, 0, readSize - nread);
				}
				this.pFdOffset += nread;
			}

			this.pOffset = windowStart;
			this.pLen = windowSize;			
			return offset - this.pOffset;
		}

		public bool UnMapFile()
		{
			return this.status;
		}

		private void FillMem(ref byte[] data, int offset, byte val, int n)
		{
			for(int i = 0; i < n; i++)
				data[offset + i]  = val;
		}

		private void MemMove(ref byte[] dest, byte[] src, int srcInd, int n)
		{
			byte[] srcCopy = (byte[])src.Clone();
			for(int i = 0; i < n; i++)
				dest[i] = srcCopy[srcInd + i];
		}

		public static void ReallocArray(ref byte[] arr,  int size)
		{
			if(arr == null)
				arr = new byte[size];
			else
			{
				byte[] arr2 = new byte[arr.Length];
				arr.CopyTo(arr2,0);
				arr = new byte[size];
				arr2.CopyTo(arr,0);
			}
		}

		public static void ReallocArrayString(ref string[] arr,  int size)
		{
			if(arr == null)
				arr = new string[size];
			else
			{
				string[] arr2 = new string[arr.Length];
				arr.CopyTo(arr2,0);
				arr = new string[size];
				arr2.CopyTo(arr,0);
			}
		}
	}
}
