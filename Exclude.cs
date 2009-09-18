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
using NetSync;

namespace NetSync
{
	public class ExcludeStruct
	{
		public string pattern;
		public UInt32 matchFlags;
		public int slashCnt;

		public ExcludeStruct(string pattern, UInt32 matchFlags, int slashCnt)
		{
			this.pattern = pattern;
			this.matchFlags  = matchFlags;
			this.slashCnt = slashCnt;
		}

		public ExcludeStruct() {}
	}

	public class Exclude
	{
		private Options options;

		public Exclude(Options opt)
		{
			options = opt;
		}

		public static void AddCvsExcludes() {}
		public void AddExcludeFile(ref ArrayList exclList, string fileName, int xFlags)
		{
			bool wordSplit = (xFlags & Options.XFLG_WORD_SPLIT) != 0;
			TextReader f;
			// TODO: path length
			if(fileName == null || fileName.CompareTo("") == 0 || !FileSystem.File.Exists(fileName))
				return;
			if(fileName.CompareTo("-") == 0)
				f = System.Console.In;
			else
				try
				{
					f = new System.IO.StreamReader(fileName);
				}
				catch
				{
					if((xFlags & Options.XFLG_FATAL_ERRORS) != 0)
					{						
						Log.Write("failed to open " + (((xFlags & Options.XFLG_DEF_INCLUDE) != 0) ? "include" : "exclude") + " file " + fileName);
					}
					return;
				}
			while(true)
			{
				string line = f.ReadLine();
				if(line == null)
					break;
				if(line.CompareTo("") != 0 && (wordSplit || (line[0] != ';' && line[0] != '#')))
					AddExclude(ref exclList, line, xFlags);
			}
			f.Close();

		}
		public void AddExclude(ref ArrayList exclList, string pattern, int xFlags)
		{
			UInt32 mFlags;
			if (pattern == null)
				return;
			string cp = pattern;
			int len = 0;
			while(true) 
			{
				if(len >= cp.Length)
					break;
				cp = GetExcludeToken(cp.Substring(len), out len,out mFlags, xFlags);
				if(len == 0)
					break;
				if((mFlags & Options.MATCHFLG_CLEAR_LIST) != 0)
				{
					if(options.verbose > 2)
						Log.WriteLine( "["+options.WhoAmI() + "] clearing exclude list");
					exclList.Clear();
					continue;
				}

				MakeExlude(ref exclList, cp, mFlags);
				if(options.verbose > 2)
					Log.WriteLine("["+options.WhoAmI() + "] AddExclude(" + cp + ")");
			}
		}

		public void MakeExlude(ref ArrayList exclList, string pat, UInt32 mFlags)
		{
			int exLen = 0;
			int patLen = pat.Length;
			ExcludeStruct ret = new ExcludeStruct();
			if(options.excludePathPrefix != null)
				mFlags |= Options.MATCHFLG_ABS_PATH;
			if(options.excludePathPrefix != null && pat[0] == '/')
				exLen = options.excludePathPrefix.Length;
			else
				exLen = 0;
			ret.pattern = "";
			if(exLen != 0)
				ret.pattern += options.excludePathPrefix;
			ret.pattern += pat.Replace('\\','/');
			patLen += exLen;
			
			if(ret.pattern.IndexOfAny(new char[] {'*','[','?'}) != -1)
			{
				mFlags |= Options.MATCHFLG_WILD;
				if(ret.pattern.IndexOf("**") != -1)
				{
					mFlags |= Options.MATCHFLG_WILD2;
					if(ret.pattern.IndexOf("**") == 0)
						mFlags |= Options.MATCHFLG_WILD2_PREFIX;
				}
			}

			if(patLen > 1 && ret.pattern[ret.pattern.Length - 1] == '/')
			{
				ret.pattern = ret.pattern.Remove(ret.pattern.Length-1,1);
				mFlags |= Options.MATCHFLG_DIRECTORY;
			}

			for(int i = 0; i < ret.pattern.Length; i++)
				if(ret.pattern[i] == '/')
					ret.slashCnt++;
			ret.matchFlags = mFlags;
			exclList.Add(ret);
		}

		static string GetExcludeToken(string p, out int len, out uint mFlags, int xFlags)
		{
			len = 0;
			string s = p;
			mFlags = 0;
			if(p.CompareTo("") == 0) return "";

			if((xFlags & Options.XFLG_WORD_SPLIT) != 0)
			{
				p = s = p.Trim(' ');				
			}
			if( (xFlags & Options.XFLG_WORDS_ONLY) == 0 && (s[0] == '-' || s[0] == '+') && s[1] == ' ')
			{
				if(s[0] == '+')
					mFlags |= Options.MATCHFLG_INCLUDE;
				s = s.Substring(2);
			} 
			else if ((xFlags & Options.XFLG_DEF_INCLUDE) != 0)
				mFlags |= Options.MATCHFLG_INCLUDE;
			if ((xFlags & Options.XFLG_DIRECTORY) != 0)
				mFlags |= Options.MATCHFLG_DIRECTORY;
			if(( xFlags & Options.XFLG_WORD_SPLIT) != 0)
			{
				int i = 0;
				while(i < s.Length && s[i] == ' ')
					i++;
				len = s.Length - i;
			} 
			else
				len = s.Length;
			if(p[0]=='!' && len ==1)
				mFlags |=  Options.MATCHFLG_CLEAR_LIST;
			return s;
		}
		
		/*
		* Return -1 if file "name" is defined to be excluded by the specified
		* exclude list, 1 if it is included, and 0 if it was not matched.
		*/
		public int CheckExclude(ArrayList listp, string name, int nameIsDir)
		{			
			foreach (ExcludeStruct ex in listp) 
			{
				if (CheckOneExclude(name, ex, nameIsDir)) 
				{
					ReportExcludeResult(name, ex, nameIsDir);
					return (ex.matchFlags & Options.MATCHFLG_INCLUDE) != 0 ? 1 : -1;
				}	
			}
			return 0;			
		}

		static bool CheckOneExclude(string name, ExcludeStruct ex, int nameIsDir)
		{			
			int match_start = 0;
			string pattern = ex.pattern;

			if (name.CompareTo("") == 0)
				return false;
			if (pattern.CompareTo("") == 0)
				return false;

			if (0 != (ex.matchFlags & Options.MATCHFLG_DIRECTORY) && nameIsDir == 0)
				return false;

			if (pattern[0] == '/') 
			{
				match_start = 1;
				pattern = pattern.TrimStart('/');
				if (name[0] == '/')
					name = name.TrimStart('/');					
			}

			if ((ex.matchFlags & Options.MATCHFLG_WILD) != 0) 
			{
				/* A non-anchored match with an infix slash and no "**"
				 * needs to match the last slash_cnt+1 name elements. */
				if (match_start != 0 && ex.slashCnt != 0 && 0 ==(ex.matchFlags & Options.MATCHFLG_WILD2)) 
				{					
					name = name.Substring(name.IndexOf('/') + 1);					
				}
				if (WildMatch.CheckWildMatch(pattern, name))
					return true;
				if ((ex.matchFlags & Options.MATCHFLG_WILD2_PREFIX) != 0) 
				{
					/* If the **-prefixed pattern has a '/' as the next
					* character, then try to match the rest of the
					* pattern at the root. */
					if (pattern[2] == '/' && WildMatch.CheckWildMatch(pattern.Substring(3), name))
						return true;
				}
				else if (0 == match_start && (ex.matchFlags & Options.MATCHFLG_WILD2) != 0) 
				{
					/* A non-anchored match with an infix or trailing "**"
					* (but not a prefixed "**") needs to try matching
					* after every slash. */
					int posSlash;
					while ((posSlash = name.IndexOf('/')) != -1) 
					{
						name = name.Substring(posSlash + 1);
						if (WildMatch.CheckWildMatch(pattern, name))
							return true;
					}
				}
			} 
			else if (match_start != 0) 
			{
				if (name.CompareTo(pattern) == 0)
					return true;
			} 
			else 
			{
				int l1 = name.Length;
				int l2 = pattern.Length;
				if (l2 <= l1 &&
					name.Substring(l1-l2).CompareTo(pattern) == 0 &&
					(l1==l2 || name[l1-(l2+1)] == '/')) 
				{
					return true;
				}
			}

			return false;
		}

		public void ReportExcludeResult(string name, ExcludeStruct ent, int nameIsDir)
		{
			/* If a trailing slash is present to match only directories,
			* then it is stripped out by make_exclude.  So as a special
			* case we add it back in here. */

			if (options.verbose >= 2) 
			{
				Log.Write(options.WhoAmI() + " "+ ((ent.matchFlags & Options.MATCHFLG_INCLUDE) != 0 ? "in" : "ex") +
					"cluding " + (nameIsDir != 0 ? "directory" : "file") + " " +
					name + " because of " + ent.pattern + " pattern " +
					((ent.matchFlags & Options.MATCHFLG_DIRECTORY) != 0 ? "/" : "") + "\n");
			}
		}

		public void SendExcludeList(IOStream f)
		{						
			if (options.listOnly && !options.recurse)
				AddExclude(ref options.excludeList,"/*/*",0);				

			foreach (ExcludeStruct ent in options.excludeList) 			
			{
				int l;				
				string p;

				if(ent.pattern.Length == 0 || ent.pattern.Length > Options.MAXPATHLEN)
					continue;
				l = ent.pattern.Length;
				p = ent.pattern;				
				if ((ent.matchFlags & Options.MATCHFLG_DIRECTORY) != 0) 
				{
					p += "/\0";					
				}

				if ((ent.matchFlags & Options.MATCHFLG_INCLUDE) != 0) 
				{
					f.writeInt(l + 2);
					f.IOPrintf("+ ");
				} 
				else if ((p[0] == '-' || p[0] == '+') && p[1] == ' ') 
				{
					f.writeInt(l + 2);
					f.IOPrintf("- ");
				} 
				else 
					f.writeInt(l);
				f.IOPrintf(p);
				
			}
			f.writeInt(0);
		}

		public void ReceiveExcludeList(IOStream f)		
		{			
			string line = "";
			int l;
			while ((l = f.readInt()) != 0) 
			{
				if (l >= Options.MAXPATHLEN+3)
				{
					Log.Write("overflow: recv_exclude_list");
					continue;
				}

				line = f.ReadSBuf(l);
				AddExclude(ref options.excludeList, line, 0);				
			}
		}
	}	
}
