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
using System.Runtime.InteropServices;
using System.Text;
using Win32;
using HANDLE = System.IntPtr;


namespace NetSync.FileSystem
{
	/// <summary>
	/// Summary description for FileSystemInfo.
	/// </summary>
	public class FileSystemInfo
	{
		protected string path;
		public FileSystemInfo(string path)
		{
			this.path = File.setUnicodePath(path);
			//
			// TODO: Add constructor logic here
			//
		}

		public FileAttributes Attributes 
		{
			get
			{
				int att = Kernel.GetFileAttributes(path);
				return (FileAttributes)att;
				// Win32API: GetFileAttributes; GetFileAttributesEx
				//return FileAttributes.ReadOnly;
			} 
		}

		public virtual bool Exists
		{
			get
			{
				WIN32_FIND_DATA p = new WIN32_FIND_DATA();
				int c = Kernel.FindFirstFile(path, ref p);
				if (c == Kernel.INVALID_HANDLE_VALUE || p.dwFileAttributes == Kernel.FILE_ATTRIBUTE_DIRECTORY) return false;
				return true;
			}
		}

		public string FullName
		{
			get 
			{
				StringBuilder s = new StringBuilder(Kernel.MAX_PATH);
				int i = Kernel.GetLongPathName(path, s, s.Capacity);
				int err=Kernel.GetLastError();
				string p= s.ToString();
				if (p.StartsWith("\\\\?\\"))
				{
					return p.Substring(4);
				}
				return p;

//				if (path.StartsWith("\\\\?\\"))
//				{
//					return path.Substring(4);
//				}
//				return path;
			}
		}
	
		public DateTime LastWriteTime 
		{
			get 
			{
				WIN32_FIND_DATA p = new WIN32_FIND_DATA();
				SYSTEMTIME st = new SYSTEMTIME();
				if(Kernel.FindFirstFile(path, ref p) != Kernel.INVALID_HANDLE_VALUE)
				{
					Kernel.FileTimeToSystemTime(ref p.ftLastWriteTime, ref st);
					DateTime time = new DateTime(st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
					return time;
				}
				return new DateTime();
			}
		}
	}
}
