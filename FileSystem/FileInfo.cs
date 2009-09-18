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

using HANDLE = System.IntPtr;

namespace NetSync.FileSystem
{
	/// <summary>
	/// Summary description for FileInfo.
	/// </summary>
	public class FileInfo : FileSystemInfo
	{
		public FileInfo(string fileName)
			: base(fileName)
		{
			//
			// TODO: Add constructor logic here
			//
		}

		public long Length 
		{
			get
			{
				Win32.WIN32_FIND_DATA o = new Win32.WIN32_FIND_DATA();
				// Win32API: GetFileSize; GetFileSizeEx
				int c = Win32.Kernel.FindFirstFile(path, ref o);
				return o.nFileSizeLow;
			}
		}
	}
}
