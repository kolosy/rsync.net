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
using System.Text;
using NetSync;

namespace NetSync
{

	public class Target 
	{
		public UInt32 t;
		public int i;
	} 

	public class TargetComparer : IComparer  
	{
		int IComparer.Compare( Object x, Object y )  
		{
			return Match.CompareTargets((Target)x,(Target)y);
		}
	}

	public class Match
	{
		public const int CHUNK_SIZE = (32*1024);
		public const int TABLESIZE = (1<<16);
		public const int NULL_TAG = -1;
		public const byte SUMFLG_SAME_OFFSET	= (1<<0) ;
		private int falseAlarms;
		private int tagHits;
		private int matches;
		private Int64 dataTransfer;

		private int totalFalseAlarms;
		private int totalTagHits;
		private int totalMatches;
		private int lastMatch;
		private ArrayList targets = new ArrayList();
		private int[] tagTable = new int[TABLESIZE];

		private Options options;		

		public Match(Options opt)
		{
			options = opt;			
		}

		public static int CompareTargets(Target t1,Target t2)
		{
			return (int)t1.t - (int)t2.t;
		}

		public static UInt32 GetTag2(UInt32 s1, UInt32 s2)
		{
			return (((s1) + (s2)) & 0xFFFF);
		}

		public static UInt32 GetTag(UInt32 sum)
		{
			return GetTag2(sum & 0xFFFF, sum >> 16);
		}

		public void BuildHashTable(SumStruct s)
		{
			for(int i = 0; i < s.count; i++)
				targets.Add(new Target());

			for (int i = 0; i < s.count; i++) 
			{
				((Target)targets[i]).i = i;
				((Target)targets[i]).t = GetTag(s.sums[i].sum1);
			}

			targets.Sort(0, s.count, new TargetComparer());

			for (int i = 0; i < TABLESIZE; i++)
				tagTable[i] = NULL_TAG;

			
			for (int i = s.count; i-- > 0;)  
			{
				tagTable[((Target)targets[i]).t] = i;
			}
		}

		public void MatchSums(IOStream f, SumStruct s, MapFile buf, int len)
		{
			byte[] fileSum = new byte[CheckSum.MD4_SUM_LENGTH];

			lastMatch = 0;
			falseAlarms = 0;
			tagHits = 0;
			matches = 0;
			dataTransfer = 0;

			Sum sum = new Sum(options);
			sum.Init(options.checksumSeed);

			if (len > 0 && s.count>0) 
			{
				BuildHashTable(s);

				if (options.verbose > 2)
					Log.WriteLine("built hash table");

				HashSearch(f,s,buf,len, sum);

				if (options.verbose > 2)
					Log.WriteLine("done hash search");
			} 
			else 
			{
				for (int j = 0; j < len - CHUNK_SIZE; j += CHUNK_SIZE) 
				{
					int n1 = Math.Min(CHUNK_SIZE,(len-CHUNK_SIZE)-j);
					Matched(f,s,buf,j+n1,-2, sum);
				}
				Matched(f,s,buf,len,-1,sum);
			}

			fileSum = sum.End();
			if (buf != null && buf.status)
				fileSum[0]++;

			if (options.verbose > 2)
				Log.WriteLine("sending fileSum");
			f.Write(fileSum, 0, CheckSum.MD4_SUM_LENGTH);

			targets.Clear();

			if (options.verbose > 2)
				Log.WriteLine("falseAlarms=" +  falseAlarms + " tagHits=" + tagHits + " matches=" + matches);

			totalTagHits += tagHits;
			totalFalseAlarms += falseAlarms;
			totalMatches += matches;
			Options.stats.literalData += dataTransfer;
		}

		public void Matched(IOStream f, SumStruct s, MapFile buf, int offset, int i, Sum sum)
		{
			int n = offset - lastMatch;
			int j;

			if (options.verbose > 2 && i >= 0)
				Log.WriteLine("match at " + offset +" last_match=" + lastMatch + " j=" + i + " len=" + s.sums[i].len + " n=" + n);

			Token token = new Token(options);
			token.SendToken(f,i,buf,lastMatch,n,(int)(i<0?0:s.sums[i].len));
			dataTransfer += n;

			if (i >= 0) 
			{
				Options.stats.matchedData += s.sums[i].len;
				n += (int)s.sums[i].len;
			}

			for (j = 0; j < n; j += CHUNK_SIZE) 
			{
				int n1 = Math.Min(CHUNK_SIZE,n-j);
				int off = buf.MapPtr(lastMatch + j, n1);
				sum.Update(buf.p , off, n1);
			}

			if (i >= 0)
				lastMatch = (int)(offset + s.sums[i].len);
			else
				lastMatch = offset;

			if (buf != null && options.doProgress) 
			{
				Progress.ShowProgress(lastMatch, buf.fileSize);
				if (i == -1)
					Progress.EndProgress(buf.fileSize);
			}
		}

		public void HashSearch(IOStream f,SumStruct s, MapFile buf, int len, Sum _sum)
		{
			int offset, end, backup;
			UInt32 k;
			int wantI;
			byte[] sum2 = new byte[CheckSum.SUM_LENGTH];
			UInt32 s1, s2, sum;
			int more;
			byte[] map;
			
			wantI = 0;
			if (options.verbose > 2)
				Log.WriteLine("hash search ob=" + s.bLength +" len=" + len);

			k = (UInt32)Math.Min(len, s.bLength);
			int off = buf.MapPtr(0, (int)k);
			map = buf.p;
			
			UInt32 g = s.sums[0].sum1;
			sum = CheckSum.GetChecksum1(map, off, (int)k);
			s1 = sum & 0xFFFF;
			s2 = sum >> 16;
			if (options.verbose > 3)
				Log.WriteLine("sum=" + sum +" k=" + k);

			offset = 0;
			end = (int)(len + 1 - s.sums[s.count-1].len);
			if (options.verbose > 3)
				Log.WriteLine("hash search s.bLength=" + s.bLength +" len=" + len +" count=" + s.count);

			do 
			{
				UInt32 t = GetTag2(s1,s2);
				bool doneCsum2 = false;
				int j = tagTable[t];

				if (options.verbose > 4)
					Log.WriteLine("offset=" + offset + " sum=" + sum);

				if (j == NULL_TAG)
					goto null_tag;

				sum = (s1 & 0xffff) | (s2 << 16);
				tagHits++;
				do 
				{
					UInt32 l;
					int i = ((Target)targets[j]).i;

					if (sum != s.sums[i].sum1)
						continue;

					l = (UInt32)Math.Min(s.bLength, len-offset);
					if (l != s.sums[i].len)
						continue;

					if (options.verbose > 3)
						Log.WriteLine("potential match at " + offset + " target=" + j +" " + i + " sum=" + sum);

					if (!doneCsum2) 
					{
						off = buf.MapPtr(offset, (int)l);
						map = buf.p;
						CheckSum cs = new CheckSum(options); 
						sum2 = cs.GetChecksum2(map, off, (int)l);
						doneCsum2 = true;
					}

					if (Util.MemCmp(sum2, 0, s.sums[i].sum2, 0, s.s2Length) != 0) 
					{
						falseAlarms++;
						continue;
					}
					
					if (i != wantI && wantI < s.count
						&& (!options.inplace || options.makeBackups || s.sums[wantI].offset >= offset
						|| (s.sums[wantI].flags & SUMFLG_SAME_OFFSET) != 0)
						&& sum == s.sums[wantI].sum1
						&& Util.MemCmp(sum2, 0, s.sums[wantI].sum2, 0, s.s2Length) == 0) 
					{
						i = wantI;
					}
				set_want_i:
					wantI = i + 1;

					Matched(f,s,buf,offset,i,_sum);
					offset += (int)(s.sums[i].len - 1);
					k = (UInt32)Math.Min(s.bLength, len-offset);
					off = buf.MapPtr(offset, (int)k);					
					sum = CheckSum.GetChecksum1(map, off, (int)k);
					s1 = sum & 0xFFFF;
					s2 = sum >> 16;
					matches++;
					break;
				} while (++j < s.count && ((Target)targets[j]).t == t);								
			null_tag:
				backup = offset - lastMatch;
				if (backup < 0)
					backup = 0;

				more = (offset + k) < len ? 1 : 0;
				off = buf.MapPtr(offset - backup, (int)(k + more + backup))+ backup;				
				s1 -= (UInt32)(CheckSum.ToInt(map[off]) + CheckSum.CHAR_OFFSET);
				s2 -= (UInt32)(k * CheckSum.ToInt(map[off]) + CheckSum.CHAR_OFFSET);				
				off = (k + off >= map.Length) ? (int)(map.Length-k-1) : off;
				if (more != 0) 
				{
					s1 += (UInt32)(CheckSum.ToInt(map[k + off]) + CheckSum.CHAR_OFFSET);
					s2 += s1;
				} 
				else
					--k;

				if (backup >= CHUNK_SIZE + s.bLength && end - offset > CHUNK_SIZE)
					Matched(f,s,buf,(int)(offset - s.bLength), -2, _sum);
			} while (++offset < end);

			Matched(f,s,buf,len,-1, _sum);
			buf.MapPtr(len-1,1);
		}

		public void MatchReport(IOStream f)
		{
			if (options.verbose <= 1)
				return;

			string report = "total: matches=" + totalMatches + "  tagHits=" + totalTagHits +"  falseAlarms=" +
				totalFalseAlarms + " data=" + Options.stats.literalData;

			Log.WriteLine(report);
			if(options.amServer)
			{
				f.MplexWrite(MsgCode.MSG_INFO,ASCIIEncoding.ASCII.GetBytes(report),report.Length);
			}
		} 
	}
}
