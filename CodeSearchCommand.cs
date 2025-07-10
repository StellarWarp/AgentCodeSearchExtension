using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.VCProjectEngine;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace AgentCodeSearchExtension
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class CodeSearchCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("07add820-1fc0-4e95-99c0-e33af0a8f2a9");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeSearchCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private CodeSearchCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static CodeSearchCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in CodeSearchCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CodeSearchCommand(package, commandService);
        }

        private void TraverseCodeElement(CodeElement element, int indent, List<string> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string indentStr = new string(' ', indent * 2);
            string lineInfo = "";
            try
            {
                if (element.StartPoint != null)
                    lineInfo = $" (line: {element.StartPoint.Line})";
            }
            catch { }

            results.Add($"{indentStr}- {element.Kind}: {element.FullName}{lineInfo}");

            // 递归遍历子元素
            if (element is CodeNamespace ns)
            {
                foreach (CodeElement child in ns.Members)
                    TraverseCodeElement(child, indent + 1, results);
            }
            else if (element is CodeType type)
            {
                foreach (CodeElement child in type.Members)
                    TraverseCodeElement(child, indent + 1, results);
            }
            else if (element is CodeFunction func)
            {
                foreach (CodeElement child in func.Parameters)
                    TraverseCodeElement(child, indent + 1, results);
            }
            else if (element is CodeProperty prop)
            {
                foreach (CodeElement child in prop.Attributes)
                    TraverseCodeElement(child, indent + 1, results);
            }
            // 其他类型可根据需要扩展
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        /// 
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = (DTE)Package.GetGlobalService(typeof(DTE)); // Fixed the issue by using Package.GetGlobalService instead of ServiceProvider.GlobalProvider
            var results = new List<string>();

            foreach (Project project in dte.Solution.Projects)
            {
                if (project.Object is VCProject vcProject)
                {
                    var codeModel = project.CodeModel;
                    foreach (CodeElement element in codeModel.CodeElements)
                    {
                        TraverseCodeElement(element, 0, results);
                    }
                }
            }

            VsShellUtilities.ShowMessageBox(
                this.package,
                string.Join("\n", results),
                "C++ Code Search",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
