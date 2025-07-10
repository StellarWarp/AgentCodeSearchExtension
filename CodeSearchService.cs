using EnvDTE;
using EnvDTE80;
using Grpc.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;



namespace AgentCodeSearchExtension
{
    public class CodeSearchService : SymbolSearchService.SymbolSearchServiceBase
    {
        private readonly AsyncPackage _package;
        DTE2 _dte;
        FindEvents _findEvents;

        public CodeSearchService(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _package = package;

        }

        public override async Task<SymbolResponse> FindSymbols(SymbolRequest request, ServerCallContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;

            var symbols = await FindCppSymbolsAsync(request.SymbolName);

            var response = new SymbolResponse();
            response.Symbols.AddRange(symbols);


            //searcher.UpdateNormalResult();
            //searcher.BeginNewNormalSearch();


            return response;
        }

        private async Task<List<SymbolInfo>> FindCppSymbolsAsync(string symbolName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var results = new List<SymbolInfo>();

            //if (await _package.GetServiceAsync(typeof(SVsGlobalSearch)) is IVsGlobalSearch globalSearch)
            //{
            //    IVsSearchQuery searchQuery = new CustomSearchQuery(symbolName);
            //    IVsGlobalSearchCallback searchCallback = new SearchCallback();
            //    IVsGlobalSearchTask searchTask = globalSearch.CreateSearch(searchQuery, searchCallback, Guid.);

            //    searchTask.Start();
            //}

            //if (await _package.GetServiceAsync(typeof(SVsIntellisenseProjectManager)) is IVsIntellisenseProjectManager vsIntellisense)
            //{

            //}



            if (await _package.GetServiceAsync(typeof(SVsObjectSearch)) is IVsObjectSearch objectSearch)
            {
                VSOBSEARCHCRITERIA[] searchCriteria = new VSOBSEARCHCRITERIA[1];
                searchCriteria[0] = new VSOBSEARCHCRITERIA
                {
                    szName = symbolName,           // 搜索字符串
                    eSrchType = VSOBSEARCHTYPE.SO_ENTIREWORD,
                    grfOptions = (int)_VSOBSEARCHOPTIONS.VSOBSO_NONE, // 无特殊选项
                    dwCustom = 0,                      // 无自定义数据
                };
                IVsObjectList resultList;

                int hresult = objectSearch.Find(
                    flags: (uint)__VSOBSEARCHFLAGS.VSOSF_NONE,
                    pobSrch: searchCriteria,
                    pplist: out resultList
                );

                if (hresult != 0 || resultList == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindCppSymbolsAsync] Find failed or resultList is null. HRESULT={hresult}, resultList={(resultList == null ? "null" : "not null")}");
                }

                uint count;
                resultList.GetItemCount(out count);

                for (uint i = 0; i < count; i++)
                {
                    resultList.GoToSource(i, VSOBJGOTOSRCTYPE.GS_DECLARATION);

                    Document activeDocument = _dte.ActiveDocument;
                    if (activeDocument == null)
                    {
                        throw new InvalidOperationException("没有活动文档");
                    }

                    TextSelection selection = activeDocument.Selection as TextSelection;
                    if (selection == null)
                    {
                        throw new InvalidOperationException("没有有效的选择");
                    }

                    FileCodeModel fileCodeModel = activeDocument.ProjectItem.FileCodeModel;
                    if (fileCodeModel == null)
                    {
                        throw new InvalidOperationException("文件不支持代码模型");
                    }

                    // 获取光标所在的 CodeElement
                    TextPoint cursorPoint = selection.ActivePoint;
                    CodeElement codeElement = fileCodeModel.CodeElementFromPoint(cursorPoint, vsCMElement.vsCMElementFunction);
                    if (codeElement is null) continue;
                    int startLine = codeElement.StartPoint.Line;
                    int endLine = codeElement.EndPoint.Line;
                    // 获取 TextDocument
                    TextDocument textDocument = activeDocument.Object("TextDocument") as TextDocument;
                    if (textDocument == null)
                    {
                        throw new InvalidOperationException("活动文档不是 TextDocument 类型");
                    }
                    int linesBefore = 5;
                    int linesAfter = 5;
                    int contextStartLine = Math.Max(1, startLine - linesBefore);
                    int contextEndLine = Math.Min(textDocument.EndPoint.Line, endLine + linesAfter);

                    // 获取上下文代码
                    EditPoint startPoint = textDocument.CreateEditPoint(textDocument.StartPoint);
                    startPoint.MoveToLineAndOffset(contextStartLine, 1);
                    EditPoint endPoint = textDocument.CreateEditPoint(textDocument.StartPoint);
                    endPoint.MoveToLineAndOffset(contextEndLine, 1);

                    string contextCode = startPoint.GetText(endPoint);

                    results.Add(new SymbolInfo
                    {
                        Name = codeElement.FullName,
                        Type = codeElement.Kind.ToString(),
                        FilePath = activeDocument.FullName,
                        LineNumber = startLine,
                    });

                    System.Diagnostics.Debug.WriteLine(
                        $"[FindCppSymbolsAsync] Found symbol: {codeElement.FullName} at {activeDocument.FullName}:{startLine}\n" +
                        $"Context Code:\n" +
                        $"{contextCode}");
                }

            }

            return results;
        }

        public override async Task<TextSearchResponse> FindText(TextSearchRequest request, ServerCallContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2;
            if (_dte is null) throw new NullReferenceException("Cant get DTE");
            _findEvents = _dte.Events.FindEvents;

            var response = new TextSearchResponse();
            var res = await TextSearchAsync(
                request.Text,
                request.SearchPath,
                request.ContextBefore,
                request.ContextBefore,
                request.FileExtension);
            response.Results.AddRange(res);
            return response;
        }



        private async Task<List<TextSearchResult>> TextSearchAsync(
            string findString,
            string searchPath,
            int contextLinesBefore = 5,
            int contextLinesAfter = 5,
            string fileType = "*.h;*.cpp"
            )
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _findEvents.FindDone += OnFindDone;

            var finder = _dte.Find as Find2;
            if (finder is null) throw new NullReferenceException();

            finder.FindWhat = findString;
            finder.SearchPath = Path.Combine(
                Path.GetDirectoryName(_dte.Solution.FileName),
                searchPath); 
            finder.SearchSubfolders = true;
            finder.Target = vsFindTarget.vsFindTargetSolution;
            finder.ResultsLocation = vsFindResultsLocation.vsFindResults1;
            finder.Action = vsFindAction.vsFindActionFindAll;
            finder.MatchCase = true;
            finder.MatchWholeWord = true;
            finder.MatchInHiddenText = false;
            finder.PatternSyntax = vsFindPatternSyntax.vsFindPatternSyntaxLiteral;
            finder.FilesOfType = fileType;
            finder.WaitForFindToComplete = true;
            vsFindResult res = finder.Execute();

            return CheckFindResultWindow(false);
        }

        private void OnFindDone(vsFindResult findRes, bool cancelled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CheckFindResultWindow(false);
            _findEvents.FindDone -= OnFindDone;
        }

        List<TextSearchResult> CheckFindResultWindow(bool limitTime)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var win = _dte.Windows.Item(EnvDTE.Constants.vsWindowKindFindResults1);
            var selection = win.Selection as EnvDTE.TextSelection;
            selection.SelectAll();
            var selectionText = selection.Text;

            List<TextSearchResult> results = new List<TextSearchResult>();
            StringReader reader = new StringReader(selectionText);
            string strReadline;
            var beginTime = DateTime.Now;
            for (int ithLine = 0; (strReadline = reader.ReadLine()) != null; ++ithLine)
            {
                if (ithLine == 0)
                {
                    continue;
                }
                int colon1Idx = strReadline.IndexOf(':');
                if (colon1Idx == -1)
                {
                    continue;
                }
                int colon2Idx = strReadline.IndexOf(':', colon1Idx + 1);
                if (colon2Idx == -1)
                {
                    continue;
                }
                var pathLineColumn = strReadline.Substring(0, colon2Idx).Trim();
                int lineBegin = pathLineColumn.LastIndexOf('(');
                int lineEnd = pathLineColumn.LastIndexOf(')');
                if (lineBegin == -1 || lineEnd == -1)
                {
                    continue;
                }
                var path = pathLineColumn.Substring(0, lineBegin);
                path = path.Replace('\\', '/');
                int fileNameBegin = path.LastIndexOf('/');
                if (fileNameBegin == -1)
                {
                    continue;
                }
                var fileName = path.Substring(fileNameBegin + 1);
                var lineStr = pathLineColumn.Substring(lineBegin + 1, lineEnd - lineBegin - 1);
                int line;
                if (!int.TryParse(lineStr, out line))
                {
                    continue;
                }
                //int column = strReadline.LastIndexOf(m_srcName);
                //if (column == -1)
                //{
                //    column = 0;
                //}
                //else
                //{
                //    column -= colon2Idx;
                //}

                TextDocument textDocument = _dte.Documents.Item(fileName).Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CheckFindResultWindow] TextDocument is null for file: {fileName}");
                    continue;
                }

                int linesBefore = 5;
                int linesAfter = 5;

                int contextStartLine = Math.Max(1, line - linesBefore);
                int contextEndLine = Math.Min(textDocument.EndPoint.Line, line + linesAfter);

                // 获取上下文代码
                EditPoint startPoint = textDocument.CreateEditPoint(textDocument.StartPoint);
                startPoint.MoveToLineAndOffset(contextStartLine, 1);
                EditPoint endPoint = textDocument.CreateEditPoint(textDocument.StartPoint);
                endPoint.MoveToLineAndOffset(contextEndLine, 1);

                string contextCode = startPoint.GetText(endPoint);

                results.Add(new TextSearchResult
                {
                    FilePath = path,
                    LineNumber = line,
                    Context = contextCode,
                });

                double duration = (DateTime.Now - beginTime).TotalMilliseconds;
                if (duration > 2000 && limitTime)
                {
                    break;
                }
            }
            reader.Close();
            return results;
        }
        
    }
}
