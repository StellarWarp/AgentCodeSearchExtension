using Grpc.Core;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace AgentCodeSearchExtension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(AgentCodeSearchExtensionPackage.PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    //[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class AgentCodeSearchExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// AgentCodeSearchExtensionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "0edeee55-259f-4cfe-a86e-27eb5eddf320";

        #region Package Members

        private uint _solutionEventsCookie = Microsoft.VisualStudio.VSConstants.VSCOOKIE_NIL;

        SolutionEventsListener _solutionEventsListener;
        private Server _grpcServer;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            //await CodeSearchCommand.InitializeAsync(this);

            try
            {
                // start server
                _grpcServer = new Server
                {
                    Services = { SymbolSearchService.BindService(new CodeSearchService(this)) },
                    Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
                };
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create gRPC server: {e.Message}");
            }
            try
            {
                _grpcServer.Start();
                System.Diagnostics.Debug.WriteLine("gRPC server started on localhost:50051");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start gRPC server: {ex.Message}");
            }

            // 获取 IVsSolution 服务
            IVsSolution solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;

            if (solution != null)
            {
                _solutionEventsListener = new SolutionEventsListener(this); // 创建并持有引用
                solution.AdviseSolutionEvents(_solutionEventsListener, out _solutionEventsCookie);
                System.Diagnostics.Debug.WriteLine("Subscribed to Solution Events.");
            }





            //var credentials = new SslServerCredentials(new[]
            //{
            //    new KeyCertificatePair(File.ReadAllText("server.crt"), File.ReadAllText("server.key"))
            //});
            //_grpcServer.Ports.Add(new ServerPort("localhost", 50051, credentials));

            await base.InitializeAsync(cancellationToken, progress);
        }
        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                _grpcServer?.ShutdownAsync().Wait();

                // 取消订阅解决方案事件
                if (_solutionEventsCookie != Microsoft.VisualStudio.VSConstants.VSCOOKIE_NIL)
                {
                    IVsSolution solution = GetService(typeof(SVsSolution)) as IVsSolution;
                    if (solution != null)
                    {
                        solution.UnadviseSolutionEvents(_solutionEventsCookie);
                        _solutionEventsCookie = Microsoft.VisualStudio.VSConstants.VSCOOKIE_NIL;
                    }
                }
                _solutionEventsListener = null;
            }
            base.Dispose(disposing);
        }

        #endregion
    }

public class SolutionEventsListener : IVsSolutionEvents
    {
        private AgentCodeSearchExtensionPackage _package;

        public SolutionEventsListener(AgentCodeSearchExtensionPackage package)
        {
            _package = package;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            Debug.WriteLine("Solution opened!");
            // 在这里添加解决方案打开后的逻辑
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            Debug.WriteLine("Solution is about to close!");
            // 在这里添加解决方案关闭前的清理逻辑
            return Microsoft.VisualStudio.VSConstants.S_OK;
        }

        // 实现 IVsSolutionEvents 接口的其他方法，如：
        public int OnAfterCloseSolution(object pUnkReserved) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) { return Microsoft.VisualStudio.VSConstants.S_OK; }
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) { return Microsoft.VisualStudio.VSConstants.S_OK; }
    }
}
