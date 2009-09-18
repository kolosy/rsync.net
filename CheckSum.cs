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

namespace NetSync
{
	class CheckSum
	{
		private Options options;

		public CheckSum(Options opt)
		{
			options = opt;
		}

		public static void SIVAL(ref byte[] buf, int pos, UInt32 x) 
		{
			buf[pos + 0] = (byte)(x & 0xFF);
			buf[pos + 1]  = (byte)( (x >> 8)& 0xFF);
			buf[pos + 2]  = (byte)( (x >> 16)& 0xFF);
			buf[pos + 3]  = (byte)( (x >> 24));
		}

		public static int ToInt(byte b)
		{
			return((b & 0x80) == 0x80) ? (b - 256) : b;				
		}

		public int cSumLength = 2;
		public const int SUM_LENGTH = 16;
		public const int CHAR_OFFSET = 0;
		public const int CSUM_CHUNK = 64;
		public const int MD4_SUM_LENGTH = 16 ;		
		
		public static UInt32 GetChecksum1(byte[] buf, int ind, int len)
		{
			Int32 i;
			UInt32 s1, s2;

			int b1 = 0, b2 = 0, b3 = 0, b4 = 0;

			s1 = s2 = 0;
			for (i = 0; i < (len-4); i+=4) 
			{
				b1 = ToInt(buf[i+0 + ind]);				
				b2 = ToInt(buf[i+1 + ind]);
				b3 = ToInt(buf[i+2 + ind]);
				b4 = ToInt(buf[i+3 + ind]);

				s2 += (UInt32)(4*(s1 + b1) + 3*b2 + 2*b3 + b4 + 10*CHAR_OFFSET);
				s1 += (UInt32)(b1 + b2 + b3 + b4 + 4*CHAR_OFFSET);
			}			
			for (; i < len; i++) 
			{				
				s1 += (UInt32)(ToInt(buf[i + ind]) + CHAR_OFFSET); 
				s2 += s1;
			}
			UInt32 sum = ((s1 & 0xffff) + (s2 << 16));
			return   sum;
		}
			
		public byte[] GetChecksum2(byte[] buf, int off, int len)
		{
			byte[] buf1 = new byte[len + 4];
			for(int j = 0; j < len ; j++)
				buf1[j] = buf[off + j];
			MDFour m = new MDFour(options);
			m.Begin();
			if(options.checksumSeed != 0)
			{
				SIVAL(ref buf1, len, (UInt32)options.checksumSeed);
				len += 4;
			}
			int i;
			for(i = 0; i + CSUM_CHUNK <= len; i += CSUM_CHUNK) 
				m.Update(buf1, i, CSUM_CHUNK);
			if (len - i > 0 || options.protocolVersion >= 27) 
				m.Update(buf1, i, (UInt32)(len-i));
			return m.Result();
		}

		public bool FileCheckSum(string fileName, ref byte[] sum, int size)
		{
			int i;
			MDFour m =new MDFour(options);
			sum = new byte[MD4_SUM_LENGTH];
			Stream fd;
			try
			{
				fd = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			} catch(Exception)
			{
				return false;
			}
			MapFile buf = new MapFile(fd, size, Options.MAX_MAP_SIZE, CSUM_CHUNK);
			m.Begin();

			for(i = 0; i + CSUM_CHUNK <= size; i += CSUM_CHUNK) 
			{
				int offset = buf.MapPtr(i, CSUM_CHUNK);
				m.Update(buf.p, offset, CSUM_CHUNK);
			}

			if (size - i > 0 || options.protocolVersion >= 27)
			{
				int offset = buf.MapPtr(i, size-i);
				m.Update(buf.p, offset, (UInt32)(size-i));
			}

			sum = m.Result();

			fd.Close();
			buf.UnMapFile();
			return true;
		}
	}

	public class MDFour
	{
		public const UInt32 MASK32 = 0xFFFFFFFF;

		public UInt32 A,B,C,D;
		public UInt32 totalN;
		public UInt32 totalN2;
		private Options options;

		public MDFour(Options opt)
		{
			options = opt;
		}


		public UInt32 F(UInt32 X, UInt32 Y, UInt32 Z)
		{
			return ((((X)&(Y)) | ((~(X))&(Z))));
		}

		public UInt32 G(UInt32 X, UInt32 Y, UInt32 Z)
		{
			return ((((X)&(Y)) | ((X)&(Z)) | ((Y)&(Z))));
		}

		public UInt32 H(UInt32 X, UInt32 Y, UInt32 Z)
		{
			return (((X)^(Y)^(Z)));
		}

		public UInt32 lshift(UInt32 x, int s)
		{
			return (((((x)<<(s))&MASK32) | (((x)>>(32-(s)))&MASK32)));
		}

		public UInt32 ROUND1(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] M, int k, int s)
		{
			return lshift((a + F(b,c,d) + M[k])&MASK32, s);
		}

		public UInt32 ROUND2(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] M, int k, int s)
		{
			return lshift((a + G(b,c,d) + M[k] + 0x5A827999)&MASK32,s);
		}

		public UInt32 ROUND3(UInt32 a, UInt32 b, UInt32 c, UInt32 d, UInt32[] M, int k, int s)
		{
			return lshift((a + H(b,c,d) + M[k] + 0x6ED9EBA1)&MASK32,s);
		}

		public void MDFour64(UInt32[] M)
		{
			UInt32 AA, BB, CC, DD;			
			AA = this.A; BB = this.B; CC = this.C; DD = this.D;

			A = ROUND1(A,B,C,D,M,  0,  3);  
			D = ROUND1(D,A,B,C,M,  1,  7);  
			C = ROUND1(C,D,A,B,M,  2, 11);  
			B = ROUND1(B,C,D,A,M,  3, 19);
			A = ROUND1(A,B,C,D,M,  4,  3);  
			D = ROUND1(D,A,B,C,M,  5,  7);  
			C = ROUND1(C,D,A,B,M,  6, 11);  
			B = ROUND1(B,C,D,A,M,  7, 19);
			A = ROUND1(A,B,C,D,M,  8,  3);  
			D = ROUND1(D,A,B,C,M,  9,  7);  
			C = ROUND1(C,D,A,B,M, 10, 11);  
			B = ROUND1(B,C,D,A,M, 11, 19);
			A = ROUND1(A,B,C,D,M, 12,  3);  
			D = ROUND1(D,A,B,C,M, 13,  7);  
			C = ROUND1(C,D,A,B,M, 14, 11);  
			B = ROUND1(B,C,D,A,M, 15, 19);	

			A = ROUND2(A,B,C,D,M,  0,  3);  
			D = ROUND2(D,A,B,C,M,  4,  5);  
			C = ROUND2(C,D,A,B,M,  8,  9);  
			B = ROUND2(B,C,D,A,M, 12, 13);
			A = ROUND2(A,B,C,D,M,  1,  3);  
			D = ROUND2(D,A,B,C,M,  5,  5);  
			C = ROUND2(C,D,A,B,M,  9,  9);  
			B = ROUND2(B,C,D,A,M, 13, 13);
			A = ROUND2(A,B,C,D,M,  2,  3);  
			D = ROUND2(D,A,B,C,M,  6,  5);  
			C = ROUND2(C,D,A,B,M, 10,  9);  
			B = ROUND2(B,C,D,A,M, 14, 13);
			A = ROUND2(A,B,C,D,M,  3,  3);  
			D = ROUND2(D,A,B,C,M,  7,  5);  
			C = ROUND2(C,D,A,B,M, 11,  9);  
			B = ROUND2(B,C,D,A,M, 15, 13);

			A = ROUND3(A,B,C,D,M,  0,  3);  
			D = ROUND3(D,A,B,C,M,  8,  9);  
			C = ROUND3(C,D,A,B,M,  4, 11);  
			B = ROUND3(B,C,D,A,M, 12, 15);
			A = ROUND3(A,B,C,D,M,  2,  3);  
			D = ROUND3(D,A,B,C,M, 10,  9);  
			C = ROUND3(C,D,A,B,M,  6, 11);  
			B = ROUND3(B,C,D,A,M, 14, 15);
			A = ROUND3(A,B,C,D,M,  1,  3);  
			D = ROUND3(D,A,B,C,M,  9,  9);  
			C = ROUND3(C,D,A,B,M,  5, 11);  
			B = ROUND3(B,C,D,A,M, 13, 15);
			A = ROUND3(A,B,C,D,M,  3,  3);  
			D = ROUND3(D,A,B,C,M, 11,  9);  
			C = ROUND3(C,D,A,B,M,  7, 11);  
			B = ROUND3(B,C,D,A,M, 15, 15);

			A += AA; B += BB; 
			C += CC; D += DD;
	
			A &= MASK32; B &= MASK32; 
			C &= MASK32; D &= MASK32;

			this.A = A; this.B = B; this.C = C; this.D = D;
		}

		public void Begin()
		{
			this.A = 0x67452301;
			this.B = 0xefcdab89;
			this.C = 0x98badcfe;
			this.D = 0x10325476;
			this.totalN = 0;
			this.totalN2 = 0;
		} 

		public byte[] Result()
		{
			byte[] ret = new byte[16];
			copy4(ref ret, 0, A);
			copy4(ref ret, 4, B);
			copy4(ref ret, 8, C);
			copy4(ref ret, 12, D);
			return ret;
		} 

		private void copy4(ref byte[] outData, int ind, UInt32 x)
		{
			outData[ind] = (byte)x;
			outData[ind + 1] = (byte)(x>>8);
			outData[ind + 2] = (byte)(x>>16);
			outData[ind + 3] = (byte)(x>>24);
		}

		private void copy64(ref UInt32[] M, int ind, byte[] inData, int ind2)
		{
			for(int i = 0; i < 16; i++)
				M[i + ind] = (UInt32)((inData[i*4+3+ind2]<<24) | (inData[i*4+2+ind2]<<16) |(inData[i*4+1+ind2]<<8) | (inData[i*4+0+ind2]<<0));
		}

		public void Tail(byte[] inData, int ind, UInt32 n)
		{
			UInt32[] M = new UInt32[16];
			this.totalN += n << 3;														
			if(this.totalN < (n << 3))
				this.totalN2++;
			this.totalN2 += n >> 29;
			byte[] buf = new byte[128];
			for(int i = 0; i < n; i++)
				buf[i] = inData[ind + i];
			buf[n] = 0x80;
			if(n <= 55) 
			{
				copy4(ref buf, 56, this.totalN);
				if(options.protocolVersion >= 27)
					copy4(ref buf, 60, this.totalN2);
				copy64(ref M, 0, buf, 0);
				MDFour64(M);
			} 
			else
			{
				copy4(ref buf, 120, this.totalN);
				if(options.protocolVersion >= 27)
					copy4(ref buf, 124, this.totalN2);
				copy64(ref M, 0, buf, 0);
				MDFour64(M);
				copy64(ref M, 0, buf, 64);
				MDFour64(M);
			}
		}

		public void Update(byte[] inData, int ind, UInt32 n)
		{
			UInt32[] M = new UInt32[16];

			if (n == 0) 
				Tail(inData, ind, 0);

			int i = 0;
			while (n >= 64) 
			{
				copy64(ref M, 0, inData, ind + i);
				MDFour64(M);
				i += 64;
				n -= 64;
				totalN += 64 << 3;
				if (totalN < 64 << 3) 
					totalN2++;
			}

			if (n != 0) 
				Tail(inData, ind + i, n);
		} 
	}

	public class Sum
	{
		public int sumResidue;
		public byte[] sumrbuf = new byte[CheckSum.CSUM_CHUNK];
		public MDFour md;
		private Options options;

		public Sum(Options opt)
		{
			options = opt;
			md = new MDFour(opt);
		}

		public void Init(int seed)
		{
			byte[] s = new byte[4];
			md.Begin();
			this.sumResidue = 0;
			CheckSum.SIVAL(ref s, 0, (UInt32)seed);
			Update(s, 0, 4);
		}

		public void Update(byte[] p, int ind, int len)
		{
			int pPos = 0;
			if (len + sumResidue < CheckSum.CSUM_CHUNK) 
			{
				for(int j = 0; j < len; j++)
					sumrbuf[sumResidue + j]  = p[j + ind];
				sumResidue += len;
				return;
			}

			if (sumResidue != 0) 
			{
				int min = Math.Min(CheckSum.CSUM_CHUNK-sumResidue,len);
				for(int j = 0; j < min; j++)
					sumrbuf[sumResidue + j]  = p[j + ind];
				md.Update(sumrbuf, 0, (UInt32)(min+sumResidue));
				len -= min;
				pPos += min;
			}

			int i;
			for(i = 0; i + CheckSum.CSUM_CHUNK <= len; i += CheckSum.CSUM_CHUNK) 
			{
				for(int j = 0; j < CheckSum.CSUM_CHUNK; j++)
					sumrbuf[j]  = p[pPos + i + j + ind];
				md.Update(sumrbuf, 0, CheckSum.CSUM_CHUNK);
			}

			if (len - i > 0) 
			{
				sumResidue = len-i;
				for(int j = 0; j < sumResidue; j++)
					sumrbuf[j]  = p[pPos + i + j + ind];
			} 
			else 
				sumResidue = 0;
		}

		public byte[] End()
		{
			if (sumResidue != 0 || options.protocolVersion >= 27) 
				md.Update(sumrbuf, 0, (UInt32)sumResidue);
			return md.Result();
		}

	}
}
