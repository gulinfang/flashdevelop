using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using ASCompletion.Context;
using System.Text.RegularExpressions;

namespace ASCompletion.Model
{
    public class InlineRange
    {
        public string Syntax;
        public int Start;
        public int End;

        public InlineRange()
        { }

        public InlineRange(string syntax, int start, int end)
        {
            Syntax = syntax;
            Start = start;
            End = end;
        }
    }

    public class ASMetaData: IComparable
    {
        static private Regex reNameTypeParams = 
            new Regex("[\"']([^\"']+)[\"']\\s*,\\s*(type|event|kind)\\s*=\\s*[\"']([^\"']+)", RegexOptions.Compiled);

        public int LineFrom;
        public int LineTo;
        public string Name;
        public Dictionary<string, string> Params;
        public string RawParams;
        public string Comments;
        public ASMetaKind Kind = ASMetaKind.Unknown;

        public ASMetaData(string name)
        {
            Name = name.Trim();
        }

        public void ParseParams(string raw)
        {
            RawParams = raw;
            Params = new Dictionary<string, string>();
            if (Enum.IsDefined(typeof(ASMetaKind), Name))
            {
                Kind = (ASMetaKind)Enum.Parse(typeof(ASMetaKind), Name);
                Match mParams = reNameTypeParams.Match(raw);
                if (mParams.Success)
                {
                    Params.Add("name", mParams.Groups[1].Value);
                    Params.Add(mParams.Groups[2].Value, mParams.Groups[3].Value);
                }
                else if (Kind == ASMetaKind.Event) // invalid Event
                    Kind = ASMetaKind.Unknown;
            }
        }

        public int CompareTo(object obj)
        {
            if (!(obj is ASMetaData))
                throw new InvalidCastException("This object is not of type ASMetaData");
            ASMetaData meta = obj as ASMetaData;
            if (Kind == ASMetaKind.Event && meta.Kind == ASMetaKind.Event)
                return Params["type"].CompareTo(meta.Params["type"]);
            return Name.CompareTo(meta.Name);
        }
    }

    public class FileModel
    {
        static public FileModel Ignore = new FileModel();

        public System.Windows.Forms.TreeState OutlineState;

        public IASContext Context;

        public bool OutOfDate;
        public DateTime LastWriteTime;
        public bool CachedModel;

        public bool HasFiltering;
        public string InlinedIn;
        public List<InlineRange> InlinedRanges;

        public bool haXe;
        public int Version;
        public string Comments;
        public string FileName;
        public string Package;
        public string FullPackage;
        public string Module;
        public bool TryAsPackage;
        public bool HasPackage;
        public int PrivateSectionIndex;
        public Dictionary<string,Visibility> Namespaces;
        public MemberList Imports;
        public List<ClassModel> Classes;
        public MemberList Members;
        public MemberList Regions;
        public List<ASMetaData> MetaDatas;

        public string BasePath
        {
            get
            {
                if (!File.Exists(FileName)) return FileName;
                string path = Path.GetDirectoryName(FileName);
                if (path.EndsWith(Package.Replace('.', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return path.Substring(0, path.Length - Package.Length);
                return path;
            }
        }

        public FileModel()
        {
            init("");
        }

        public FileModel(string fileName)
        {
            init(fileName);
        }
        public FileModel(string fileName, DateTime cacheLastWriteTime)
        {
            init(fileName);
            LastWriteTime = cacheLastWriteTime;
        }
        private void init(string fileName)
        {
            Package = "";
            Module = "";
            FileName = fileName;
            haXe = (fileName.Length > 3) ? fileName.EndsWith(".hx") : false;
            //
            Namespaces = new Dictionary<string, Visibility>();
            //
            Imports = new MemberList();
            Classes = new List<ClassModel>();
            Members = new MemberList();
            Regions = new MemberList();
        }

        public string GetBasePath()
        {
            if (FileName.Length == 0) return null;
            
            string path = Path.GetDirectoryName(FileName);
            if (Package.Length == 0 && FileName.Length > 0) return path;

            // get up the packages path
            string packPath = Path.DirectorySeparatorChar+Package.Replace('.', Path.DirectorySeparatorChar);
            if (path.ToUpper().EndsWith(packPath.ToUpper()))
            {
                return path.Substring(0, path.Length - packPath.Length);
            }
            else
            {
                return null;
            }
        }

        public void Check()
        {
            if (this == FileModel.Ignore) return;

            if (OutOfDate)
            {
                OutOfDate = false;
                if (File.Exists(FileName) && (CachedModel || LastWriteTime < System.IO.File.GetLastWriteTime(FileName)))
                    try
                    {
                        ASFileParser.ParseFile(this);
                    }
                    catch
                    {
                        OutOfDate = false;
                        Imports.Clear();
                        Classes.Clear();
                        Members.Clear();
                        PrivateSectionIndex = 0;
                        Package = "";
                    }

            }
        }

        public ClassModel GetPublicClass()
        {
            if (Classes != null)
            {
                if (Version > 3) // haXe
                {
                    var module = Module == "" ? Path.GetFileNameWithoutExtension(FileName) : Module;
                    foreach (ClassModel model in Classes)
                        if ((model.Flags & (FlagType.Class | FlagType.Interface)) > 0 && model.Name == module) return model;
                }
                else
                {
                    foreach (ClassModel model in Classes)
                        if ((model.Access & (Visibility.Public | Visibility.Internal)) > 0) return model;
                }
            }
            return ClassModel.VoidClass;
        }

        public ClassModel GetClassByName(string name)
        {
            int p = name.IndexOf('<'); 
            if (p > 0)
            {
                // remove parameters, ie. Array<T>
                if (p > 2 && name[p - 1] == '.') p--;
                name = name.Substring(0, p);
            }

            foreach (ClassModel aClass in Classes)
                if (aClass.Name == name) return aClass;
            return ClassModel.VoidClass;
        }

        /// <summary>
        /// Return a sorted list of the file
        /// </summary>
        /// <returns></returns>
        internal MemberList GetSortedMembersList()
        {
            MemberList items = new MemberList();
            items.Add(Members);
            items.Sort();
            return items;
        }

        #region Text output

        public override string ToString()
        {
            return String.Format("package {0} ({1})", Package, FileName);
        }

        public string GenerateIntrinsic(bool caching)
        {
            if (this == FileModel.Ignore) return "";

            StringBuilder sb = new StringBuilder();
            string nl = (caching) ? "" : "\r\n";
            char semi = ';';
            string tab = (caching) ? "" : "\t";

            // header
            if (Version > 2)
            {
                sb.Append("package");
                if (Package.Length > 0) sb.Append(" ").Append(Package);
                if (haXe) sb.Append(semi).Append(nl).Append(nl);
                else sb.Append(nl).Append("{").Append(nl);
            }

            // imports
            if (Imports.Count > 0)
            {
                foreach (MemberModel import in Imports)
                    sb.Append(tab).Append("import ").Append(import.Type).Append(semi).Append(nl);
                sb.Append(nl);
            }

            // event/style metadatas
            if (MetaDatas != null)
            {
                foreach (ASMetaData meta in MetaDatas)
                    if (meta.Kind == ASMetaKind.Include)
                    {
                        sb.Append(meta.RawParams).Append(nl);
                    }
                    else if (meta.Kind != ASMetaKind.Unknown)
                    {
                        sb.Append(ClassModel.CommentDeclaration(meta.Comments, tab));
                        sb.Append(tab).Append('[').Append(meta.Name).Append('(').Append(meta.RawParams).Append(")] ").Append(nl).Append(nl);
                    }
            }

            // members			
            string decl;
            foreach (MemberModel member in Members)
            {
                if ((member.Flags & FlagType.Variable) > 0)
                {
                    sb.Append(ClassModel.CommentDeclaration(member.Comments, tab));
                    sb.Append(tab).Append(ClassModel.MemberDeclaration(member)).Append(semi).Append(nl);
                }
                else if ((member.Flags & FlagType.Function) > 0)
                {
                    decl = ClassModel.MemberDeclaration(member);
                    sb.Append(ClassModel.CommentDeclaration(member.Comments, tab));
                    sb.Append(tab).Append(decl).Append(semi).Append(nl);
                }
            }

            foreach (ClassModel aClass in Classes)
            {
                sb.Append(aClass.GenerateIntrinsic(caching));
                sb.Append(nl);
            }

            if (Version == 3) sb.Append('}').Append(nl);
            return sb.ToString();
        }
        #endregion
    }
}
