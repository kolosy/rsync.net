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

namespace NetSync
{
	public class Util
	{
		static bool pushDirInitialized = false;
		public static string currDir = null;
		public static bool S_ISDIR(uint mode)
		{
			if((mode & 0x8000) == 0)
				return true;
			return false;
		}

		public static string sanitizePath(string p, string rootDir, int depth)
		{
			return p;
		}
		public static bool pushDir(string dir)
		{
			if(!pushDirInitialized)
			{
				pushDirInitialized = true;
				// TODO: path length
				currDir = FileSystem.Directory.GetCurrentDirectory();
			}

			if( dir == null || dir.CompareTo("") == 0)
				return false;
			//...
			try
			{
				// TODO: path length
				FileSystem.Directory.SetCurrentDirectory(dir);
			}
			catch
			{
				return false;
			}

			// TODO: path length
			currDir = FileSystem.Directory.GetCurrentDirectory();
			return true;
		}

		public static bool popDir(string dir)
		{
			try
			{
				// TODO: path length
				FileSystem.Directory.SetCurrentDirectory(dir);
			} 
			catch
			{
				return false;
			}
			currDir = dir;
			return true;
		} 

		public static string fullFileName(string fileName)
		{
			//...Safe FileName
			if(fileName.IndexOf(':') != -1 || fileName.StartsWith("\\\\"))
				return fileName;	//absolute
			else
				return currDir + '\\' + fileName;	//relative
			//...modules
		}

		public static string cleanFileName(string fileName, bool collapseDotDot)
		{
			string cleanedName = fileName.Replace("\\\\", "\\");
			if(cleanedName.EndsWith("\\"))
				cleanedName = cleanedName.Substring(0, cleanedName.Length - 1);
			if(collapseDotDot)
				cleanedName = cleanedName.Substring(0,2) + cleanedName.Substring(2).Replace("..","");
			return cleanedName;
		}

		public static string NS(string s)
		{
			return s == null ? "<NULL>" : s;
		}

		public static int FindColon(string s)
		{
			int index = s.IndexOf(":");
			if(index == -1)
				return -1;
			int slashIndex = s.IndexOf("/");
			if(slashIndex != -1 && slashIndex < index)
				return -1;
			return index;
		}

		public static object[] ArrayDeleteLastElement(object[] x)
		{
			object[] y = new string[x.Length - 1];
			for(int i = 0; i < y.Length; i++)
				y[i] = x[i];
			return y;
		}

		public static object[] ArrayDeleteFirstElement(object[] x)
		{
			object[] y = new string[x.Length - 1];
			for(int i = 0; i < y.Length; i++)
				y[i] = x[i + 1];
			return y;
		}

		public static int MemCmp(byte[] arr1, int off1, byte[] arr2, int off2, int length)
		{
			for(int i = 0; i < length; i++)
				if(arr1[off1 + i] != arr2[off2 + i])
					return arr1[off1 + i] - arr2[off2 + i];
			return 0;
		}

		public static void MemCpy(byte[] arr1, int off1, byte[] arr2, int off2, int length)
		{
			for(int i = 0; i < length; i++)
				arr1[off1 + i] = arr2[off2 + i];					
		}

		public static int CompareModTime(long file1, long file2, Options options)
		{
			if (file2 > file1) 
			{
				if (file2 - file1 <= options.modifyWindow)
					return 0;
				return -1;
			}
			if (file1 - file2 <= options.modifyWindow)
				return 0;
			return 1;
		}
	}
}
