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
using System;
using System.IO;
using System.Collections;

namespace NetSync
{
	public class Generator
	{
		public const int BLOCKSUM_BIAS = 10;
		private Options options;
		private CheckSum checkSum;

		public Generator(Options opt)
		{
	 		options = opt;
			checkSum = new CheckSum(options);
		}

		public void WriteSumHead(IOStream f, SumStruct sum)
		{
			if(sum == null)
				sum = new SumStruct();
			f.writeInt(sum.count);
			f.writeInt((int)sum.bLength);
			if (options.protocolVersion >= 27)
				f.writeInt(sum.s2Length);
			f.writeInt((int)sum.remainder);
		} 

		public void GenerateFiles(IOStream f, ArrayList fileList, string localName)
		{
			int i;
			int phase = 0;
			

			if (options.verbose > 2) 
				Log.WriteLine("generator starting count=" + fileList.Count);

			for (i = 0; i < fileList.Count; i++) 
			{
				FileStruct file = ((FileStruct)fileList[i]);			 
				if (file.baseName == null)
					continue;
				if(Util.S_ISDIR(file.mode))
				 		continue;
				ReceiveGenerator(localName != null ? localName : file.FNameTo() ,file, i, f);
			}

			phase++;
			checkSum.cSumLength = CheckSum.SUM_LENGTH;
			if (options.verbose > 2)
				Log.WriteLine("GenerateFiles phase=" + phase);
			f.writeInt(-1);

			phase++;
			if (options.verbose > 2)
				Log.WriteLine("GenerateFiles phase=" + phase);

			f.writeInt(-1);

			if (options.protocolVersion >= 29 && !options.delayUpdates)
				f.writeInt(-1);

			/* now we need to fix any directory permissions that were
			* modified during the transfer 
			* */
			for (i = 0; i < fileList.Count; i++) 
			{
				FileStruct file = ((FileStruct)fileList[i]);
				if (file.baseName != null || Util.S_ISDIR(file.mode))
					continue;
				ReceiveGenerator(localName != null ? localName : file.FNameTo() ,file, i, null);
			}

			if (options.verbose > 2)
				Log.WriteLine("GenerateFiles finished");
		} 

		public void ReceiveGenerator(string fileName, FileStruct file, int i, IOStream f)
		{		 		 
			fileName = Path.Combine(options.dir, fileName);		 

			if (UnchangedFile(fileName, file)) 
			{			 
				return;
			}
			if (options.verbose > 2)
				Log.WriteLine("Receive Generator(" + fileName + "," + i + ")\n"); 		 
			int statRet;
			FStat st = new FStat();
			if (options.dryRun) 
			{
				statRet = -1;		
			} 
			else 
			{
				statRet = 0;
				try
				{
					FileSystem.FileInfo fi = new FileSystem.FileInfo(fileName);
					// TODO: path length
					st.size = fi.Length;
					// TODO: path length
					st.mTime = fi.LastWriteTime;
				}
				catch
				{
					statRet = -1;
				}			 
			}

			if (options.onlyExisting && statRet == -1) 
			{
				/* we only want to update existing files */
				if (options.verbose > 1) 
					Log.WriteLine("not creating new file \"" + fileName + "\"");
				return;
			}
			string fNameCmp = fileName;
			if(options.wholeFile > 0)
			{
				f.writeInt(i);
				WriteSumHead(f, null);
				return;
			} 
			FileStream fd;
			try
			{
				fd = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
			}
			catch
			{
				if (options.verbose > 3) 
					Log.WriteLine("failed to open " + Util.fullFileName(fNameCmp) + ", continuing");			 
				f.writeInt(i);
				WriteSumHead(f, null);
				return;
			}
				
			if (options.verbose > 3) 
				Log.WriteLine("gen mapped " + fNameCmp + " of size " + st.size);

			if (options.verbose > 2)
				Log.WriteLine("generating and sending sums for " + i);

			f.writeInt(i);
			Stream fCopy = null;
			GenerateAndSendSums(fd, st.size, f, fCopy);

			if (fCopy != null) 
			{
				fCopy.Close();			 
			}
			fd.Close(); 
		}

		public void GenerateAndSendSums(Stream fd, long len, IOStream f, Stream fCopy)
		{
			long i;
			MapFile mapBuf;
			SumStruct sum = new SumStruct();
			long offset = 0;			

			SumSizesSqroot(sum, (UInt64)len);

			if (len > 0)
				mapBuf = new MapFile(fd, (int)len, Options.MAX_MAP_SIZE, (int)sum.bLength);
			else
				mapBuf = null;

			WriteSumHead(f, sum);

			for (i = 0; i < sum.count; i++) 
			{
				UInt32 n1 = (UInt32)Math.Min(len, sum.bLength);
				int off = mapBuf.MapPtr((int)offset, (int)n1);
				byte[] map = mapBuf.p;
				UInt32 sum1 = CheckSum.GetChecksum1(map, off, (int)n1);
				byte[] sum2 = new byte[CheckSum.SUM_LENGTH];

				sum2 = checkSum.GetChecksum2(map, off, (int)n1);
				if (options.verbose > 3) 
					Log.WriteLine("chunk[" + i + "] offset=" + offset + " len=" + n1 +" sum1=" + sum1);
				f.writeInt((int)sum1);
				f.Write(sum2, 0, sum.s2Length);
				len -= n1;
				offset += n1;
			}
			if (mapBuf != null)
				mapBuf = null;
		}

		public void SumSizesSqroot(SumStruct sum, UInt64 len)
		{
			UInt32 bLength;
			int s2Length;
			UInt32 c;
			UInt64 l;			

			if (options.blockSize != 0) 
			{
				bLength = (UInt32)options.blockSize;
			} 
			else 
				if (len <= Options.BLOCK_SIZE * Options.BLOCK_SIZE) 
			{
				bLength = Options.BLOCK_SIZE;
			} 
			else 
			{
				l = len;
				c = 1;
				while ((l = (l >> 1)) != 0) 
				{
					c <<= 1;
				}
				bLength = 0;
				do 
				{
					bLength |= c;
					if (len < bLength * bLength)
						bLength &= ~c;
					c >>= 1;
				} while (c >= 8);	/* round to multiple of 8 */
				bLength = Math.Max(bLength, Options.BLOCK_SIZE);
			}

			if (options.protocolVersion < 27) 
				s2Length = checkSum.cSumLength; 
			else 
				if (checkSum.cSumLength == CheckSum.SUM_LENGTH) 
			{
				s2Length = CheckSum.SUM_LENGTH;
			} 
			else 
			{
				int b = BLOCKSUM_BIAS;
				l = len;
				while ((l = (l >> 1)) != 0) 
				{
					b += 2;
				}
				c = bLength;
				while ((c = (c >> 1)) != 0 && b != 0) 
				{
					b--;
				}
				s2Length = (b + 1 - 32 + 7) / 8;
				s2Length = Math.Max(s2Length, checkSum.cSumLength);
				s2Length = Math.Min(s2Length, CheckSum.SUM_LENGTH);
			}

			sum.fLength	= (int)len;
			sum.bLength	= bLength;
			sum.s2Length	= s2Length;
			sum.count	= (int)((len + (bLength - 1)) / bLength);
			sum.remainder	= (UInt32)(len % bLength);

			if (sum.count != 0 && options.verbose > 2) 
				Log.WriteLine("count="+sum.count+" rem="+sum.remainder+" blength="+sum.bLength+
					" s2length="+sum.s2Length+" flength="+sum.fLength);
		} 

		/* Perform our quick-check heuristic for determining if a file is unchanged. */
		public bool UnchangedFile(string fileName, FileStruct file)
		{			
			// TODO: path length
			if(!FileSystem.File.Exists(fileName))
				return false;
			
			FileSystem.FileInfo fi = new FileSystem.FileInfo(fileName);
			// TODO: path length
			if (fi.Length != file.length)
				return false;

			/* if always checksum is set then we use the checksum instead
			of the file time to determine whether to sync */
			if (options.alwaysChecksum) 
			{
				byte[] sum = new byte[CheckSum.MD4_SUM_LENGTH];
				// TODO: path length
				checkSum.FileCheckSum(fileName, ref sum, (int)fi.Length);
				return Util.MemCmp(sum, 0, file.sum, 0, options.protocolVersion < 21 ? 2 : CheckSum.MD4_SUM_LENGTH) == 0;
			}

			if (options.sizeOnly)
				return true;

			if (options.ignoreTimes)
				return false;

			// TODO: path length
			return Util.CompareModTime(fi.LastWriteTime.Second, file.modTime.Second, options) == 0;			
		}
	}
}
