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

namespace NetSync
{
	public class FileStruct
	{
		public int length;
		public string baseName;
		public string dirName;
		public string baseDir;
		public System.DateTime modTime;
		public uint mode;
		public int uid;
		public int gid;
		public uint flags;
		public bool isTopDir;
		public byte[] sum;	

		public string FNameTo()
		{
			string fullName = "";		
	
			if(baseName == null || baseName.CompareTo("") == 0)
				baseName = null;
			if(dirName == null || dirName.CompareTo("") == 0)
				dirName = null;
								
			if(dirName != null && baseName != null)			
				fullName = dirName + "/" + baseName;			 
			else if(baseName != null)
				fullName = baseName; 
			else if(dirName != null)
				fullName = dirName; 
			return fullName;

		}		
	}

	public class FileStructComparer : IComparer  
	{
		int IComparer.Compare( Object x, Object y )  
		{
			return FileList.fileCompare((FileStruct)x,(FileStruct)y);
		}
	}

	public class FileList
	{
		private string lastDir = "";
		private string fileListDir = "";
		private string lastName = "";
		private UInt32 mode = 0;
		private DateTime modTime = DateTime.Now;							
		private Options options;
		private CheckSum checkSum;

		public FileList(Options opt)
		{
			options = opt;
			checkSum = new CheckSum(options);
		}
		
		public ArrayList sendFileList(ClientInfo cInfo, string[] argv)
		{
			IOStream f = null;
			if(cInfo != null)
				f = cInfo.IoStream;

			string dir ="", olddir =""; 
			string lastPath = "";
			string fileName = "";
			bool useFFFD = false;
			if(showFileListP() && f != null)
				startFileListProgress("building file list");
			Int64 startWrite = Options.stats.totalWritten;
			ArrayList fileList = new ArrayList();			
			if(f != null)
			{
				f.IOStartBufferingOut();
				if(Options.filesFromFD != null)
				{					
					if(argv[0] != null && argv[0].CompareTo("") != 0 && !Util.pushDir(argv[0]))
					{						
						MainClass.Exit("pushDir "+ Util.fullFileName(argv[0]) +" failed", cInfo);
					}
					useFFFD = true;
				}
			}			
			while(true)
			{
				if(useFFFD)
				{
					if((fileName = f.readFilesFromLine(Options.filesFromFD, options)).Length == 0)
						break;					
				} 
				else
				{
					if(argv.Length == 0)
						break;
					fileName = argv[0];
					argv = (string[])Util.ArrayDeleteFirstElement(argv);
					if(fileName != null && fileName.CompareTo(".") == 0)
						continue;					
					if(fileName != null)
						fileName = fileName.Replace(@"\","/");
				}
				// TODO: path length
				if(FileSystem.Directory.Exists(fileName) && !options.recurse && options.filesFrom == null)
				{
					Log.WriteLine("skipping directory " + fileName);
					continue;
				}		

				dir = null;
				olddir = "";

				if(options.relativePaths == 0)
				{
					int index = fileName.LastIndexOf('/');
					if(index != -1)
					{
						if(index == 0)
							dir = "/";
						else
							dir = fileName.Substring(0, index);
						fileName = fileName.Substring(index + 1);
					} 
				} 
				else 
				{
					if( f != null && options.impliedDirs && fileName.LastIndexOf('/') > 0)
					{
						string fn = fileName.Substring(0, fileName.LastIndexOf('/'));
						string slash = fileName;
						int i = 0;
						while( i < fn.Length && i < lastPath.Length && fn[i] == lastPath[i])
						{
							if(fn[i] == '/')
								slash = fileName.Substring(i);
							i++;
						}
						if(i != fileName.LastIndexOf('/') || ( i < lastPath.Length && lastPath[i] != '/'))
						{
							bool copyLinksSaved = options.copyLinks;
							bool recurseSaved = options.recurse;
							options.copyLinks = options.copyUnsafeLinks;
							options.recurse = true;
							int j;
							while((j = slash.IndexOf('/')) != -1) 
							{
								sendFileName(f, fileList, fileName.Substring(0, j), false, 0);
								slash = slash.Substring(0,j) + ' ' + slash.Substring(j+1);

							}
							options.copyLinks = copyLinksSaved;
							options.recurse  = recurseSaved;
							lastPath = fileName.Substring(0,i);
						}
					}
				}
				if(dir != null && dir != "")
				{
					olddir = Util.currDir;
					if(!Util.pushDir(dir))
					{
						Log.WriteLine("pushDir " + Util.fullFileName(dir) + " failed");
						continue;
					}
					if(lastDir != null && lastDir.CompareTo(dir) == 0)
						fileListDir = lastDir;
					else
						fileListDir = lastDir = dir;
				}
				sendFileName( f, fileList, fileName, options.recurse, Options.XMIT_TOP_DIR);
				if(olddir != null && olddir != "")
				{
					fileListDir = null;
					if(Util.popDir(olddir))
					{							
						MainClass.Exit("pop_dir "+Util.fullFileName(dir)+" failed", cInfo);
					}
				}
			}
			if( f != null)
			{
				sendFileEntry(null, f, 0);
				if(showFileListP())
					finishFileListProgress(fileList);
			}			
			cleanFileList(fileList, false, false);			
			if(f != null)
			{				
				f.writeInt(0);				
				Options.stats.flistSize = (int)(Options.stats.totalWritten - startWrite);
				Options.stats.numFiles = fileList.Count;
			}
			
			if(options.verbose > 3)
				outputFileList(fileList);
			if(options.verbose > 2)
				Log.WriteLine("sendFileList done");
			return fileList;
		}

		public ArrayList receiveFileList(ClientInfo cInfo)
		{
			IOStream f = cInfo.IoStream;			
			ArrayList fileList = new ArrayList();

			if(showFileListP())
				startFileListProgress("receiving file list");

			Int64 startRead = Options.stats.totalRead;			

			UInt32 flags;			
			while((flags = f.readByte()) != 0)
			{				
				if (options.protocolVersion >= 28 && (flags & Options.XMIT_EXTENDED_FLAGS) != 0)
					flags |= (UInt32)(f.readByte() << 8);
				FileStruct file = receiveFileEntry(flags,cInfo);
				if(file == null)
					continue;
				fileList.Add(file);
				Options.stats.totalSize += ((FileStruct)fileList[fileList.Count-1]).length;
				mayBeEmitFileListProgress(fileList);
				if(options.verbose > 2)
					Log.WriteLine("receiveFileName(" + ((FileStruct)fileList[fileList.Count-1]).FNameTo() + ")");
			}
			receiveFileEntry(0, null);

			if(options.verbose > 2)
				Log.WriteLine("received " + fileList.Count + " names");

			if(showFileListP())
				finishFileListProgress(fileList);
			
			cleanFileList(fileList, (options.relativePaths == 0) ? false : true, true);			

			if ( f != null)
			{				
				f.readInt();
			}

			if (options.verbose > 3)
				outputFileList(fileList);
			if(options.listOnly)
				for(int i = 0; i < fileList.Count; i++)
					listFileEntry((FileStruct)fileList[i]);
			if(options.verbose > 2)
				Log.WriteLine("receiveFileList done");

			Options.stats.flistSize = (int)(Options.stats.totalRead - startRead);
			Options.stats.numFiles = fileList.Count;
			return fileList;
		}

		public static int fileCompare(FileStruct file1, FileStruct file2)
		{			
			return uStringCompare(file1.FNameTo(),file2.FNameTo());			
		}

		public static int uStringCompare(string s1, string s2)
		{			
			int i = 0;
			while(s1.Length > i && s2.Length > i && s1[i] == s2[i])
			{				
				i++;
			}			

			if ((s2.Length == s1.Length) && (s1.Length == i) && (s2.Length == i))
				return 0;

			if(s1.Length == i)
				return - (int)s2[i];
			if(s2.Length == i)
				return (int)s1[i];			
			return (int)s1[i] - (int)s2[i];
		}		

		public static int fileListFind(ArrayList fileList, FileStruct file)
		{
			int low = 0, high = fileList.Count - 1;
			while(high >= 0 && ((FileStruct)fileList[high]).baseName == null) high--;
			if (high < 0)
				return -1;
			while(low != high)
			{
				int mid = (low + high) / 2;
				int ret = fileCompare((FileStruct)fileList[fileListUp(fileList, mid)],file);
				if(ret == 0)
					return fileListUp(fileList,mid);
				if(ret > 0)
					high = mid;
				else
					low = mid + 1;
			}

			if(fileCompare((FileStruct)fileList[fileListUp(fileList, low)],file) == 0)
				return fileListUp(fileList, low);
			return -1;
		}

		static int fileListUp(ArrayList fileList, int i)
		{
			while (((FileStruct)fileList[i]).baseName == null) i++;
			return i;
		} 

		public void outputFileList(ArrayList fileList)
		{
			string uid = "", gid = "";
			for(int i = 0; i< fileList.Count; i++)
			{
				FileStruct file = (FileStruct)fileList[i];
				if((options.amRoot || options.amSender) && options.preserveUID)
					uid = " uid=" + file.uid;
				if( options.preserveGID && file.gid != Options.GID_NONE )
					gid = " gid=" + file.gid;
				Log.WriteLine("[" + options.WhoAmI() + "] i=" + i + " " + Util.NS(file.baseDir) + " " +
					Util.NS(file.dirName) + " " + Util.NS(file.baseName) + " mode=0" + Convert.ToString(file.mode,8) +
					" len=" + file.length + uid + gid);
			}
		}

		public void sendFileName(IOStream f, ArrayList fileList, string fileName, bool recursive, UInt32 baseFlags)
		{
			FileStruct file = makeFile(fileName, fileList, f == null && options.deleteExcluded ? Options.SERVER_EXCLUDES : Options.ALL_EXCLUDES);
			if(file == null)
				return;
			mayBeEmitFileListProgress(fileList);
			if(file.baseName != null && file.baseName.CompareTo("") != 0)
			{
				fileList.Add(file);
				sendFileEntry(file, f, baseFlags );
				
				if(recursive && Util.S_ISDIR(file.mode) && (file.flags & Options.FLAG_MOUNT_POINT) == 0)
				{								
					options.localExcludeList.Clear();
					sendDirectory(f, fileList, file.FNameTo());
				}
			}
		}

		public FileStruct makeFile(string fileName, ArrayList fileList, int excludeLevel)
		{
			if(fileName == null || fileName.CompareTo("") == 0) return null;
			string thisName = Util.cleanFileName(fileName,false);
			if(options.sanitizePath)
				thisName = Util.sanitizePath(thisName, "", 0);
			FileStruct fs = new FileStruct();
			// TODO: path length
			if(FileSystem.Directory.Exists(thisName))
			{									
				if(thisName.LastIndexOf('/') != -1)
				{
					thisName = thisName.TrimEnd('/');
					fs.dirName = thisName.Substring(0,thisName.LastIndexOf('/')).Replace(@"\","/");
					fs.baseName = thisName.Substring(thisName.LastIndexOf('/') + 1);				
					fs.gid = 0;
					fs.uid = 0;
					fs.mode = 0x4000 | 0x16B ;
					// TODO: path length
					FileSystem.DirectoryInfo di = new FileSystem.DirectoryInfo(thisName);
					if((di.Attributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
						fs.mode |= 0x92;
				}

			}
			// TODO: path length
			if(FileSystem.File.Exists(thisName))
			{
				
				if (excludeLevel != Options.NO_EXCLUDES && checkExcludeFile(thisName, 0, excludeLevel))
					return null;				
				fs.baseName = Path.GetFileName(thisName);
				fs.dirName = FileSystem.Directory.GetDirectoryName(thisName).Replace(@"\","/").TrimEnd('/');				
				FileSystem.FileInfo fi = new FileSystem.FileInfo(thisName);
				
				// TODO: path length
				fs.length = (int)fi.Length;
				// TODO: path length
				fs.modTime = fi.LastWriteTime;	
				fs.mode = 0x8000 | 0x1A4 ; 
				// TODO: path length
				if((FileSystem.File.GetAttributes(thisName) & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
				   fs.mode |= 0x92;
				fs.gid = 0;
				fs.uid = 0;
				
				int sum_len = options.alwaysChecksum ? CheckSum.MD4_SUM_LENGTH : 0;				
				if(sum_len != 0)
					if(!checkSum.FileCheckSum(thisName, ref fs.sum, fs.length))
					{
						Log.Write("Skipping file " + thisName);
						return null;
					}

				Options.stats.totalSize += fs.length;

			}
			fs.baseDir = fileListDir;			
			return fs;
		}

		public FileStruct receiveFileEntry(UInt32 flags, ClientInfo cInfo)
		{			
			if(cInfo == null)
			{
				lastName = "";
				return null;
			}
			IOStream f = cInfo.IoStream;

			int l1=0, l2=0;			
			
			if ((flags & Options.XMIT_SAME_NAME) != 0)
				l1 = f.readByte();

			if ((flags & Options.XMIT_LONG_NAME) != 0)
				l2 = f.readInt();
			else
				l2 = f.readByte();
			if (l2 >= Options.MAXPATHLEN - l1) 
			{				
				MainClass.Exit("overflow: lastname=" + lastName, cInfo);
			}

			string thisName = lastName.Substring(0, l1);			
			thisName += f.ReadSBuf(l2);
			lastName = thisName;
			
			thisName = Util.cleanFileName(thisName, false);
			if(options.sanitizePath)
				thisName = Util.sanitizePath(thisName, "", 0);

			string baseName = "";
			string dirName = "";			
			if (thisName.LastIndexOf("/") != -1) 
			{
				baseName = Path.GetFileName(thisName);				
				dirName = FileSystem.Directory.GetDirectoryName(thisName).Replace(@"\","/").Replace(":","");
			} 
			else 
			{
				baseName = thisName;
				dirName = null;
			}

			Int64 fileLength = f.ReadLongInt();
			
			if ((flags & Options.XMIT_SAME_TIME) == 0)
				modTime = DateTime.FromFileTime(f.readInt());
			if ((flags & Options.XMIT_SAME_MODE) == 0)				
				mode = (UInt32)f.readInt();

			if (options.preserveUID && (flags & Options.XMIT_SAME_UID) == 0)
			{
				int uid = f.readInt();
			}
			if (options.preserveGID && (flags & Options.XMIT_SAME_GID) == 0)
			{
				int gid = f.readInt();
			}

			byte[] sum = new byte[0];
			if (options.alwaysChecksum && !Util.S_ISDIR(mode)) 
			{		
				sum = new byte[CheckSum.MD4_SUM_LENGTH];										
				sum = f.ReadBuf(options.protocolVersion < 21 ? 2 : CheckSum.MD4_SUM_LENGTH);
			}
 
			FileStruct fs = new FileStruct();
			fs.length = (int)fileLength;
			fs.baseName = baseName;
			fs.dirName = dirName;
			fs.sum = sum;
			fs.mode = mode;
			fs.modTime = modTime;
			fs.flags = flags;
			return fs;
		}

		public void sendFileEntry(FileStruct file, IOStream f, UInt32 baseflags)
		{
			UInt32 flags = baseflags;
			int l1 = 0,l2 = 0;

			if(f == null)
				return;
			if(file == null)
			{
				f.writeByte(0);
				lastName = "";
				return;
			}
			string fileName = file.FNameTo().Replace(":","");
			for (l1 = 0;
				lastName.Length >l1 && (fileName[l1] == lastName[l1]) && (l1 < 255);
				l1++) {}
			l2 = fileName.Substring(l1).Length;

			flags |= Options.XMIT_SAME_NAME;			

			if (l2 > 255)
				flags |= Options.XMIT_LONG_NAME; 
			if(options.protocolVersion >= 28)
			{
				if(flags == 0 && !Util.S_ISDIR(file.mode))
					flags |= Options.XMIT_TOP_DIR; 
				/*if ((flags & 0xFF00) > 0 || flags == 0) 
				{
					flags |= Options.XMIT_EXTENDED_FLAGS;
					f.writeByte((byte)flags);
					f.writeByte((byte)(flags >> 8));
				} 
				else					*/
					f.writeByte((byte)flags); 
			} else 
			{
				if ((flags & 0xFF) == 0 && !Util.S_ISDIR(file.mode))
					flags |= Options.XMIT_TOP_DIR;
				if ((flags & 0xFF) == 0)
					flags |= Options.XMIT_LONG_NAME;
				f.writeByte((byte)flags);
			}
			if ((flags & Options.XMIT_SAME_NAME) != 0)
				f.writeByte((byte)l1);
			if ((flags & Options.XMIT_LONG_NAME) != 0)
				f.writeInt(l2);
			else
				f.writeByte((byte)l2); 	
			
			
			byte[] b =System.Text.ASCIIEncoding.ASCII.GetBytes(fileName);

			f.Write(b, l1, l2);
			f.WriteLongInt(file.length); 

			
			if ((flags & Options.XMIT_SAME_TIME) == 0)
				f.writeInt(file.modTime.Second);
			if ((flags & Options.XMIT_SAME_MODE) == 0)
				f.writeInt((int)file.mode);
			if (options.preserveUID && (flags & Options.XMIT_SAME_UID) == 0) 
			{				
				f.writeInt(file.uid);
			}
			if (options.preserveGID && (flags & Options.XMIT_SAME_GID) == 0) 
			{				
				f.writeInt(file.gid);
			}
			if (options.alwaysChecksum ) 
			{
				byte[] sum;	
				if(!Util.S_ISDIR(file.mode))
					sum = file.sum;				
				else if(options.protocolVersion < 28)
					sum = new byte[16];
				else 
					sum = null;

				if (sum != null ) 
				{										
					f.Write(sum, 0, options.protocolVersion < 21 ? 2 : CheckSum.MD4_SUM_LENGTH);
				}

			}

			lastName = fileName;
		}

		public void sendDirectory(IOStream f, ArrayList fileList, string dir)
		{
			FileSystem.DirectoryInfo di = new FileSystem.DirectoryInfo(dir);
			if(di.Exists)
			{
				if(options.cvsExclude)
				{
					Exclude excl = new Exclude(options);
					excl.AddExcludeFile(ref options.localExcludeList, dir, (int)(Options.XFLG_WORD_SPLIT & Options.XFLG_WORDS_ONLY));
				}
				FileSystem.FileInfo[] files = di.GetFiles();
				for(int i=0; i< files.Length; i++)
					// TODO: path length
					sendFileName(f, fileList,files[i].FullName.Replace("\\", "/"), options.recurse,0);
				FileSystem.DirectoryInfo[] dirs = di.GetDirectories();
				for(int i=0; i< dirs.Length; i++)
					// TODO: path length
					sendFileName(f, fileList,dirs[i].FullName.Replace("\\", "/"), options.recurse,0);
			} 
			else
			{
				Log.WriteLine("Can't find directory '" + Util.fullFileName(dir) + "'");
				return;
			}
		}

		public void cleanFileList(ArrayList fileList, bool stripRoot, bool noDups)
		{			
			if(fileList == null || fileList.Count == 0)
				return;
			fileList.Sort(new FileStructComparer());			
			for(int i = 0; i< fileList.Count; i++)	 
			{
				if(fileList[i] == null)
					fileList.RemoveAt(i);				
			}
			if(stripRoot)
			{
				for(int i = 0; i <fileList.Count; i++)
				{
					if(((FileStruct)fileList[i]).dirName != null && ((FileStruct)fileList[i]).dirName[0] == '/')
						((FileStruct)fileList[i]).dirName = ((FileStruct)fileList[i]).dirName.Substring(1);
					if(((FileStruct)fileList[i]).dirName != null && ((FileStruct)fileList[i]).dirName.CompareTo("") == 0)
						((FileStruct)fileList[i]).dirName = null;
				}

			}
			return;
		}

		private bool showFileListP()
		{
			return (options.verbose != 0) && (options.recurse || options.filesFrom!= null) && !options.amServer;
		}

		private void startFileListProgress(string kind)
		{
			Log.Write(kind + " ...");
			if (options.verbose > 1 || options.doProgress)
				Log.WriteLine("");
		}

		private void finishFileListProgress(ArrayList fileList)
		{
			if (options.doProgress) 
				Log.WriteLine(fileList.Count.ToString() + " file"+ (fileList.Count == 1 ? " " : "s ") + "to consider"); 
			else
				Log.WriteLine("Done.");
		} 

		private void mayBeEmitFileListProgress(ArrayList fileList)
		{
			if(options.doProgress && showFileListP() && (fileList.Count % 100) == 0)
				EmitFileListProgress(fileList);
		}

		private void EmitFileListProgress(ArrayList fileList)
		{
			Log.WriteLine(" " + fileList.Count + " files...");
		}

		private void listFileEntry(FileStruct fileEntry)
		{
			if(fileEntry.baseName == null || fileEntry.baseName.CompareTo("") == 0)
				return;			
			string perms = "";			
			Log.WriteLine(perms + " " + fileEntry.length + " " + fileEntry.modTime.ToString() + " " + fileEntry.FNameTo());
		}

		/*
		 * This function is used to check if a file should be included/excluded
		 * from the list of files based on its name and type etc.  The value of
		 * exclude_level is set to either SERVER_EXCLUDES or ALL_EXCLUDES.
		 */
		private bool checkExcludeFile(string fileName, int isDir, int excludeLevel)
		{
			int rc;

			if (excludeLevel == Options.NO_EXCLUDES)
				return false;
			if (fileName.CompareTo("") != 0) 
			{
				/* never exclude '.', even if somebody does --exclude '*' */
				if (fileName[0] == '.' && fileName.Length == 1)
					return false;
				/* Handle the -R version of the '.' dir. */
				if (fileName[0] == '/') 
				{
					int len = fileName.Length;
					if (fileName[len-1] == '.' && fileName[len-2] == '/')
						return true;
				}
			}
			if (excludeLevel != Options.ALL_EXCLUDES)
				return false;
			Exclude excl = new Exclude(options);
			if (options.excludeList.Count > 0
				&& (rc = excl.CheckExclude(options.excludeList, fileName, isDir)) != 0)
				return (rc < 0) ? true : false;
			return false;
		}
	}
}
