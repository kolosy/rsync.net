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
using System.Runtime.InteropServices;
using System.Text;
using Win32;

namespace NetSync.FileSystem
{
	/// <summary>
	/// Summary description for Directory.
	/// </summary>
	public class Directory
	{
		public static bool Exists(string path)
		{
			// Win32API: FindFirstFile
			if (path != null)
			{
				WIN32_FIND_DATA p = new WIN32_FIND_DATA();
				int c = Win32.Kernel.FindFirstFile(setUnicodePath(path), ref p);
				if (c == Win32.Kernel.INVALID_HANDLE_VALUE || p.dwFileAttributes != Kernel.FILE_ATTRIBUTE_DIRECTORY) return false;
				return true;
			}
			return false;
		}
//
//		public static void CreateDirectory(string path)
//		{
//			Win32.SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
//			Win32.Kernel.CreateDirectory(setUnicodePath(path), ref sa);
//			int error = Win32.Kernel.GetLastError();
//			// Win32API: CreateDirectory
//		}

		public static bool CreateDirectory(string path)
		{
			if (path != null)
			{
				string[] args = path.Split(':');

				string[] dirs = args[1].Replace(":", "").Split('/');
				string newpath = args[0]+":";
				for(int i=1; i<dirs.Length; i++)
				{
					newpath = newpath +"/" + dirs[i];
					SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
					Kernel.CreateDirectory(setUnicodePath(newpath), ref sa);
					Console.WriteLine(Kernel.GetLastError());
				}

				return true;
			}
			return false;
		}

		public static string GetDirectoryName(string path)
		{
			if (path != null)
			{
				string[] dirs = path.Split('/');
				string dirName="";
				if (dirs.Length>2)
				{
					dirName = path.Substring(0, path.Length-dirs[dirs.Length-1].Length-1);
				}
				return dirName;
			}
			return "";
		}
		public static void Delete(string path)
		{
			if (path!=null) Win32.Kernel.RemoveDirectory(setUnicodePath(path));
			// Win32API: RemoveDirectory
		}

		public static void SetCurrentDirectory(string path)
		{
			if (path!=null) Win32.Kernel.SetCurrentDirectory(setUnicodePath(path));
			// Win32API: SetCurrentDirectory
		}

		public static string GetCurrentDirectory()
		{
			int bufferLength = 32767;
			StringBuilder lpBuffer= new StringBuilder(bufferLength);
			Win32.Kernel.GetCurrentDirectory(bufferLength, lpBuffer);
			// Win32API: GetCurrentDirectory
			return lpBuffer.ToString();
		}

		public static string setUnicodePath(string path)
		{
			if (path != null)
			{
				path="\\\\?\\"+path.Replace("/","\\");
				if (path.LastIndexOf("\\")==path.Length-1)
					path = path.Substring(0, path.Length-1);
			}
			return path;
		}
	}
}
