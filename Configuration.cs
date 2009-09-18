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
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NetSync
{	
	public class Configuration
	{
		private string confFile;

		public ArrayList Modules = null;
		public string logFile;
		public string port;
		public string address;

		public Configuration(string cFile)
		{			
			confFile = Path.Combine(Environment.SystemDirectory,Path.GetFileName(cFile));			
		}

		public int GetNumberModule(string nameModule)
		{
			lock(this)
			{
				for(int i=0; i < Modules.Count; i++)
					if(((Module)Modules[i]).Name == nameModule)
						return i;
			}
			return -1;
		}

		public Module GetModule(int numberModule)
		{
			lock(this)
			{
				if(numberModule < 0 || numberModule > Modules.Count)
					return null;
				return (Module)Modules[numberModule];
			}
		}
		
		public string GetModuleName(int numberModule){
			lock(this)
				return GetModule(numberModule).Name;
		}

		public bool ModuleIsReadOnly(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).ReadOnly;
		}

		public bool ModuleIsWriteOnly(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).WriteOnly;
		}

		public string GetHostsAllow(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).HostsAllow;
		}

		public string GetHostsDeny(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).HostsDeny;
		}

		public string GetAuthUsers(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).AuthUsers;
		}

		public string GetSecretsFile(int numberModule)
		{
			lock(this)
				return GetModule(numberModule).SecretsFile;
		}

		public bool LoadParm(Options options)
		{
			lock(this)
			{
				TextReader cf;
				// TODO: path length
				if(confFile == null || confFile.CompareTo("") == 0 || !FileSystem.File.Exists(confFile))
				{
					MainClass.Exit("Can't find .conf file: " + confFile, null);
					return false;			
				}
				try
				{
					cf = new System.IO.StreamReader(confFile);
				}
				catch
				{				
					MainClass.Exit("failed to open: " + confFile, null);
					return false;
				}

				Module mod = null;
			
				if(Modules == null)
					Modules = new ArrayList();
				lock(cf)
				{
					while(true)
					{
						string line = cf.ReadLine();
						if(line == null)
							break;
						line = line.Trim();
						if(line.CompareTo("") != 0 && line[0] != ';' && line[0] != '#')
						{
							if(line[0] == '[' && line[line.Length - 1] == ']')
							{
								line = line.TrimStart('[').TrimEnd(']');
								int numberModule = -1;
								if((numberModule = GetNumberModule(line)) >= 0)
								{ 
									mod = GetModule(numberModule);
								}
								else 
								{
									mod = new Module(line);
									Modules.Add(mod);
								}
							} 
							else 
							{
								if(mod != null)
								{
									string[] parm = line.Split('=');
									if(parm.Length > 2)
										continue;
									parm[0] = parm[0].Trim().ToLower();
									parm[1] = parm[1].Trim();
									switch(parm[0])
									{
										case "path":
											mod.Path = parm[1].Replace(@"\","/");
											break;
										case "comment":
											mod.Comment = parm[1];
											break;
										case "read only":
											mod.ReadOnly = (parm[1].CompareTo("false") == 0) ? false : true;
											break;
										case "write only":
											mod.WriteOnly = (parm[1].CompareTo("true") == 0) ? true : false;
											break;
										case "hosts allow":
											mod.HostsAllow = parm[1];
											break;
										case "hosts deny":
											mod.HostsDeny = parm[1];
											break;
										case "auth users":
											mod.AuthUsers = parm[1];
											break;
										case "secrets file":
											mod.SecretsFile = Path.GetFileName(parm[1]);
											break;
										default:
											continue;
									}
								} 
								else
								{
									string[] parm = line.Split('=');
									if(parm.Length > 2)
										continue;
									parm[0] = parm[0].Trim();
									parm[1] = parm[1].Trim();
									switch(parm[0])
									{
										case "log file":
											string logFile = parm[1];
											try
											{
												options.logFile = new FileStream(logFile, FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write);
											}										
											catch(Exception e)
											{
												Log.Write(e.Message);
											}																				
											break;
										case "port":
											port = parm[1];									
											options.rsyncPort = Convert.ToInt32(port);
											break;
										case "address":
											options.bindAddress = address = parm[1];
											break;
										default:
											continue;
									}
								}
							}
						}
					}
					cf.Close();
				}
			}
			return true;	
		}
	}

	public class Module
	{
		public string Name;
		public string Path = "";
		public string Comment = "";	
		public bool ReadOnly = true;
		public bool WriteOnly = false;
		public string HostsAllow = "";
		public string HostsDeny = "";
		public string AuthUsers = "";
		public string SecretsFile = "";		
		
		public Module(string name)
		{
			Name = name;
		}
	}


}
