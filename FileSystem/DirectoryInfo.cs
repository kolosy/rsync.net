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
using System.Collections;
using System.IO;
using Win32;
using HANDLE = System.IntPtr;

namespace NetSync.FileSystem
{
	/// <summary>
	/// Summary description for DirectoryInfo.
	/// </summary>
	public class DirectoryInfo : FileSystemInfo
	{
		public DirectoryInfo(string path)
			: base(path)
		{
			//
			// TODO: Add constructor logic here
			//
		}
		public override bool Exists
		{
			get
			{
				WIN32_FIND_DATA p = new WIN32_FIND_DATA();
				int c = Kernel.FindFirstFile(path, ref p);
				if (c == Kernel.INVALID_HANDLE_VALUE || p.dwFileAttributes != Kernel.FILE_ATTRIBUTE_DIRECTORY) return false;
				return true;
			}
		}
		public DirectoryInfo[] GetDirectories() 
		{
			bool finished = false;
			ArrayList diList = new ArrayList();
			string searchPath="";
			if(path.LastIndexOf("\\")==path.Length-1)
				searchPath=path+"*";
			else searchPath=path+@"\*";

			WIN32_FIND_DATA fileData = new WIN32_FIND_DATA();
			HANDLE hSearch = (HANDLE)Win32.Kernel.FindFirstFile(searchPath, ref fileData);
			if (hSearch.ToInt32()==Win32.Kernel.INVALID_HANDLE_VALUE)
				return null;
			if(fileData.dwFileAttributes == Win32.Kernel.FILE_ATTRIBUTE_DIRECTORY)
			{
				diList.Add(new DirectoryInfo(path.TrimEnd('\\')+"\\"+fileData.cFileName));
			}
			while(!finished)
			{
				if (Win32.Kernel.FindNextFile(hSearch, ref fileData)!=0) 
				{
					if(fileData.dwFileAttributes == Win32.Kernel.FILE_ATTRIBUTE_DIRECTORY)
					{
						diList.Add(new DirectoryInfo(path.TrimEnd('\\')+"\\"+fileData.cFileName));
					}
				}
				else
				{
					finished=!finished;
				}
			}
			DirectoryInfo[] di = new DirectoryInfo[diList.Count];
			diList.CopyTo(di);
			return di;
		}

		public FileInfo[] GetFiles() 
		{
			bool finished = false;
			string searchPath="";
			ArrayList fiList = new ArrayList();
			WIN32_FIND_DATA fileData = new WIN32_FIND_DATA();
			if(path.LastIndexOf("\\")==path.Length-1)
				searchPath=path+"*";
			else searchPath=path+@"\*";
			HANDLE hSearch = (HANDLE)Win32.Kernel.FindFirstFile(searchPath, ref fileData);
			if (hSearch.ToInt32() == Win32.Kernel.INVALID_HANDLE_VALUE)
				return null;
			if(fileData.dwFileAttributes != Win32.Kernel.FILE_ATTRIBUTE_DIRECTORY)
			{
				fiList.Add(new FileInfo(path.TrimEnd('\\')+"\\"+fileData.cFileName));
				Console.WriteLine(fileData.cFileName);
			}
			while(!finished)
			{
				if (Win32.Kernel.FindNextFile(hSearch, ref fileData)!=0) 
				{
					if(fileData.dwFileAttributes != Win32.Kernel.FILE_ATTRIBUTE_DIRECTORY)
					{
						fiList.Add(new FileInfo(path.TrimEnd('\\')+"\\"+fileData.cFileName));
					}
				}
				else
				{
					int err = Win32.Kernel.GetLastError();
					finished=!finished;
				}
			}
			FileInfo[] fi = new FileInfo[fiList.Count];
			fiList.CopyTo(fi);
			return fi;
		}

//		private string CreatePath(string fName)
//		{
//			if (path.)
//			return "";
//		}
	}
}
