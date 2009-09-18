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
using System.Text.RegularExpressions;

namespace NetSync
{	
	public class WildMatch
	{

		static int ABORT_ALL = -1;
		static int ABORT_TO_STARSTAR = -2;
		static Char NEGATE_CLASS = '!';

		/* Find the pattern (pattern) in the text string (text). */
		public static bool CheckWildMatch(string pattern, string text)
		{		
			return	DoMatch(pattern, text) == 1;
		}

		public static bool CC_EQ(string cclass, string litmatch)
		{
			return	cclass.CompareTo(litmatch) == 0;
		} 
		private static int DoMatch(string p, string text)
		{
			int matched, special;
			Char ch, prev;			
			for (int k = 0; k < p.Length; k++) {
				ch = p[k];				
				if(k > 0) 
					if(text.Length > 1)
						text = text.Substring(1);
					else 
						return 0;
				switch (ch) {
				case '\\':
					/* Literal match with following character.  Note that the test
					* in "default" handles the p[1] == '\0' failure case. */
					ch = p[++k];
					/* FALLTHROUGH */
					goto default;
				default:
					if (text[0] != ch)
						return 0;
					continue;
				case '?':
					/* Match anything but '/'. */
					if (text[0] == '/')
						return 0;
					continue;
				case '*':					
					if (k + 1 < p.Length){											
						if(p[++k] == '*') 
						{ 
							while (p[++k] == '*') {}
							special = 1;
						} else
						{
							special = 0;
						}
					}
					else
						special = 0;
					if (p[k] == '\0') {						
						return (special == 1) ? 1 : (text.IndexOf('/')== -1 ? 0 : 1);
					}
					if(p.CompareTo("*") == 0) text = text.Substring(1);
					string r = p.Substring(k);
					for (int t = 0; t < text.Length;){					
						if ((matched = DoMatch(r, text)) != 0) {
							if (special == 0 || matched != ABORT_TO_STARSTAR)
								return 1;
						} else if (special == 0 && text[0] == '/')
							return ABORT_TO_STARSTAR;
						text = text.Substring(1);
					}
					return ABORT_ALL;
				case '[':					
						k++;
					/* Assign literal true/false because of "matched" comparison. */
					special = ch == NEGATE_CLASS ? 1 : 0;
					if (special == 1) {
						/* Inverted character class. */
						ch = p[++k];
					}
					prev = Char.MinValue;
					matched= 0;
					do {
					if (k >= p.Length)
						return ABORT_ALL;
					if (ch == '\\') {
						ch = p[++k];
						if (k > p.Length)
							return ABORT_ALL;
						if (text[0] == ch)
							matched = 1;
					}
					else if (ch == '-' && prev != Char.MinValue && p.Length - k > 1 && p[1] != ']') {
						ch = p[++k];
						if (ch == '\\') {
							ch = p[++k];
						if (k >= p.Length)
							return ABORT_ALL;
						}
						if (text[0].CompareTo(ch) <= 0 && text[0].CompareTo(prev) >= 0)
							matched = 1;
						ch = Char.MinValue; /* This makes "prev" get set to 0. */
					}
					else if (ch == '[' && p[k] == ':') {												
						int j = 0;
						ch = p[k+j+1];
						while (p.Length > k+1+j && ch != ']')
						{
							j++;
							ch = p[k+1+j];
						}
						if (k+1 >= p.Length)
							return ABORT_ALL;						
						if (j == 0 || p[k+j] != ':') {
							/* Didn't find ":]", so treat like a normal set. */							
							ch = '[';
							if (text[0] == ch)
								matched = 1;
							continue;
						}  else
						{
							k += j;
						}
						string s = p.Substring(k-j+1,j-1);
						if (CC_EQ(s,"alnum")) {
						if (Char.IsLetterOrDigit(text[0]))
							matched = 1;
						}
						else if (CC_EQ(s,"alpha")) {
						if (Char.IsLetter(text[0]))
							matched = 1;
						}				
						else if (CC_EQ(s,"blank")) {
						if (Char.IsWhiteSpace(text[0]))
							matched = 1;
						}						
						else if (CC_EQ(s,"digit")) {
						if (Char.IsDigit(text[0]))
							matched = 1;
						}					
						else if (CC_EQ(s,"lower")) {
						if (Char.IsLower(text[0]))
							matched = 1;
						}						
						else if (CC_EQ(s,"punct")) {
						if (Char.IsPunctuation(text[0]))
							matched = 1;
						}
						else if (CC_EQ(s,"space")) {
						if (Char.IsWhiteSpace(text[0]))
							matched = 1;
						}
						else if (CC_EQ(s,"upper")) {
						if (Char.IsUpper(text[0]))
							matched = 1;
						}
						else if (CC_EQ(s,"xdigit")) {
						if (Char.IsSurrogate(text[0]))
							matched = 1;
						}
						else
						return ABORT_ALL;
						ch = Char.MinValue;
					}
					else if (text[0] == ch)
						matched = 1;
					prev = ch;
					} while ((ch = p[++k]) != ']');
					if (matched == special || text[0] == '/')
						return 0;
					continue;
				}				
			}
			text = text.Substring(1);
			return text.CompareTo("") != 0 ? 0 : 1;
		}
	}
}
