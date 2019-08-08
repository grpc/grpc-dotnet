﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Grpc.Dotnet.Cli.Properties {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "16.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class CoreStrings {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal CoreStrings() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Grpc.Dotnet.Cli.Properties.CoreStrings", typeof(CoreStrings).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The access modifier to use for the generated C# classes. Default value is Public..
        /// </summary>
        internal static string AccessOptionDescription {
            get {
                return ResourceManager.GetString("AccessOptionDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The protobuf file reference(s). These can be a path to glob for local protobuf file(s)..
        /// </summary>
        internal static string AddFileCommandArgumentDescription {
            get {
                return ResourceManager.GetString("AddFileCommandArgumentDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Add protobuf file reference(s) to the gRPC project..
        /// </summary>
        internal static string AddFileCommandDescription {
            get {
                return ResourceManager.GetString("AddFileCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Additional directories to be used when resolving imports for the protobuf files. This is a semicolon separated list of paths..
        /// </summary>
        internal static string AdditionalImportDirsOption {
            get {
                return ResourceManager.GetString("AdditionalImportDirsOption", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The URL to a remote protobuf file..
        /// </summary>
        internal static string AddUrlCommandArgumentDescription {
            get {
                return ResourceManager.GetString("AddUrlCommandArgumentDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Add a protobuf url reference to the gRPC project..
        /// </summary>
        internal static string AddUrlCommandDescription {
            get {
                return ResourceManager.GetString("AddUrlCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Output a list of file(s) that will be updated without downloading any new content..
        /// </summary>
        internal static string DryRunOptionDescription {
            get {
                return ResourceManager.GetString("DryRunOptionDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Found more than one project in `{0}`. Please specify which one to use..
        /// </summary>
        internal static string ErrorMoreThanOneProjectFound {
            get {
                return ResourceManager.GetString("ErrorMoreThanOneProjectFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to A output path must be specified when adding a URL reference via the &apos;-o|--output&apos; option..
        /// </summary>
        internal static string ErrorNoOutputProvided {
            get {
                return ResourceManager.GetString("ErrorNoOutputProvided", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not find any project in `{0}`. Please specify a project explicitly..
        /// </summary>
        internal static string ErrorNoProjectFound {
            get {
                return ResourceManager.GetString("ErrorNoProjectFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Output path `{0}` is invalid. The path cannot be a directory path and must be a file path..
        /// </summary>
        internal static string ErrorOutputMustBeFilePath {
            get {
                return ResourceManager.GetString("ErrorOutputMustBeFilePath", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The project {0} does not exist..
        /// </summary>
        internal static string ErrorProjectDoesNotExist {
            get {
                return ResourceManager.GetString("ErrorProjectDoesNotExist", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The reference {0} does not exist..
        /// </summary>
        internal static string ErrorReferenceDoesNotExist {
            get {
                return ResourceManager.GetString("ErrorReferenceDoesNotExist", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The reference being added is not a valid URL..
        /// </summary>
        internal static string ErrorReferenceNotUrl {
            get {
                return ResourceManager.GetString("ErrorReferenceNotUrl", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to List the protobuf reference(s) of the gRPC project..
        /// </summary>
        internal static string ListCommandDescription {
            get {
                return ResourceManager.GetString("ListCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Adding file reference {0}..
        /// </summary>
        internal static string LogAddFileReference {
            get {
                return ResourceManager.GetString("LogAddFileReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Adding required gRPC package reference: {0}..
        /// </summary>
        internal static string LogAddPackageReference {
            get {
                return ResourceManager.GetString("LogAddPackageReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Adding file reference {0} with content from {1}..
        /// </summary>
        internal static string LogAddUrlReference {
            get {
                return ResourceManager.GetString("LogAddUrlReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Updating content of {0} with content at {1}..
        /// </summary>
        internal static string LogDownload {
            get {
                return ResourceManager.GetString("LogDownload", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to File reference: {0}.
        /// </summary>
        internal static string LogListFileReference {
            get {
                return ResourceManager.GetString("LogListFileReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Protobuf references:.
        /// </summary>
        internal static string LogListHeader {
            get {
                return ResourceManager.GetString("LogListHeader", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to URL reference: {0} from {1}.
        /// </summary>
        internal static string LogListUrlReference {
            get {
                return ResourceManager.GetString("LogListUrlReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Removing reference to file {0}..
        /// </summary>
        internal static string LogRemoveReference {
            get {
                return ResourceManager.GetString("LogRemoveReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Content of {0} is identical to the content at {1}, skipping..
        /// </summary>
        internal static string LogSkipDownload {
            get {
                return ResourceManager.GetString("LogSkipDownload", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not find a reference for the file `{0}`..
        /// </summary>
        internal static string LogWarningCouldNotFindFileReference {
            get {
                return ResourceManager.GetString("LogWarningCouldNotFindFileReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Could not find a reference that uses the source url `{0}`..
        /// </summary>
        internal static string LogWarningCouldNotFindRemoteReference {
            get {
                return ResourceManager.GetString("LogWarningCouldNotFindRemoteReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to No file found matching file argument `{0}`. File reference not added..
        /// </summary>
        internal static string LogWarningNoReferenceResolved {
            get {
                return ResourceManager.GetString("LogWarningNoReferenceResolved", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The reference {0} does not reference a .proto file. This may lead to compilation errors..
        /// </summary>
        internal static string LogWarningReferenceNotProto {
            get {
                return ResourceManager.GetString("LogWarningReferenceNotProto", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Specify the download path for the remote protobuf file. This is a required option..
        /// </summary>
        internal static string OutputOptionDescription {
            get {
                return ResourceManager.GetString("OutputOptionDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The path to the project file to operate on. If a file is not specified, the command will search the current directory for one..
        /// </summary>
        internal static string ProjectOptionDescription {
            get {
                return ResourceManager.GetString("ProjectOptionDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The URL(s) or file path(s) to remote protobuf references(s) that should be updated. Leave this argument empty to refresh all remote references..
        /// </summary>
        internal static string RefreshCommandArgumentDescription {
            get {
                return ResourceManager.GetString("RefreshCommandArgumentDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Check remote protobuf references(s) for updates and replace them if a newer version is available. If no file or url is provided, all remote protobuf files will be updated..
        /// </summary>
        internal static string RefreshCommandDescription {
            get {
                return ResourceManager.GetString("RefreshCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The URL(s) or file path(s) of the protobuf references to remove..
        /// </summary>
        internal static string RemoveCommandArgumentDescription {
            get {
                return ResourceManager.GetString("RemoveCommandArgumentDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Remove protobuf references(s) from the gRPC project..
        /// </summary>
        internal static string RemoveCommandDescription {
            get {
                return ResourceManager.GetString("RemoveCommandDescription", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The type of gRPC services that should be generated. If Default is specified, Both will be used for Web projects and Client will be used for non-Web projects..
        /// </summary>
        internal static string ServiceOptionDescription {
            get {
                return ResourceManager.GetString("ServiceOptionDescription", resourceCulture);
            }
        }
    }
}
