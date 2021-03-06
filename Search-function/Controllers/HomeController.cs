using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.Mvc;
using Search_function.Models;
using HtmlAgilityPack;
using Lucene.Net.Documents;
using System.Text.RegularExpressions;
using Lucene.Net.Search;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search.Highlight;
using System.Data;
using Microsoft.VisualBasic;

namespace Search_function.Controllers
{
    public class HomeController : Controller
    {
        readonly static SimpleFSLockFactory _LockFactory = new SimpleFSLockFactory();
        //GET: Home
        public ActionResult test()
        {
            return View();
        }

        public ActionResult Search()
        {
            var path = Server.MapPath("/Index-lucene");
            int numberOfFiles = System.IO.Directory.GetFiles(path).Length;
            var searchText = HttpUtility.UrlDecode(Request.QueryString.ToString());
            string output = searchText.Substring(searchText.IndexOf('=') + 1);
            string searchWord = output.Replace('+', ' ');
            ViewBag.YourSearch = searchWord;
            if (numberOfFiles != 0 && output.Length > 0)
            {
                Lucene.Net.Store.Directory dir = FSDirectory.Open(path); // index 파일 가져옴
                Analyzer analyzer = new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
                IndexReader indexReader = IndexReader.Open(dir, true); //IndexReader, IndexSearcher, IndexWriter 는 하나의 인덱스 파일만 바라봄  

                Searcher indexSearch = new IndexSearcher(indexReader);

                try
                {
                    var startSearchTime = DateTime.Now.TimeOfDay;
                    string totaltimeTakenToSearch = string.Empty;

                    var queryParser = new MultiFieldQueryParser(Lucene.Net.Util.Version.LUCENE_29, new string[] { "metaTag", "prevewContent", "fileNameWithoutExtension" }, analyzer);
                    var query = queryParser.Parse(searchWord); // 조회시 사용할 쿼리 작업 
                    //ViewBag.SearchQuery = "Searching for: \"" + searchWord + "\"";

                    TopDocs resultDocs = indexSearch.Search(query, indexReader.NumDocs()); //Doc index로 조회한 후 결과정보가 담긴 document 
                    TopScoreDocCollector collector = TopScoreDocCollector.Create(20000, true);
                    ViewBag.SearchQuery = resultDocs.TotalHits + " result(s) found for \"" + searchWord + "\"";

                    
                    indexSearch.Search(query, collector); //query필요한 문서를 찾아서 정보를 collector 에게 넘겨줌
                    ScoreDoc[] hits = collector.TopDocs().ScoreDocs; // collector가 문서를 추출 

                    IFormatter formatter = new SimpleHTMLFormatter("<span style=\"color: black; font-weight: bold;\">", "</span>"); 
                    SimpleFragmenter fragmenter = new SimpleFragmenter(160);
                    QueryScorer scorer = new QueryScorer(query);
                    Highlighter highlighter = new Highlighter(formatter, scorer);
                    highlighter.TextFragmenter = fragmenter; //highlighter.SetTextFragmenter(fragmenter);
                    List<ListofResult> parts = new List<ListofResult>();
                    for (int i = 0; i < hits.Length; i++)
                    {
                        int docId = hits[i].Doc; // resultDocs .ScoreDocs[0].Doc 을 사용 해도 될 듯 ? 
                        float score = hits[i].Score;
                        Document doc = indexSearch.Doc(docId); // 문서 아이디로 해당 문서 조회 
                        string url = doc.Get("URL");
                        string title = doc.Get("filename");
                        TokenStream stream = analyzer.TokenStream("", new StringReader(doc.Get("prevewContent")));
                        string content = highlighter.GetBestFragments(stream, doc.Get("prevewContent"), 3, "..."); //검색문장 강조 
                        if (content == null || content == "") // 
                        {
                            string contents = doc.Get("prevewContent");
                            if (contents != "")
                            {
                                if (contents.Length < 480)
                                {
                                    content = contents.Substring(0, contents.Length);
                                }
                                else
                                {
                                    content = contents.Substring(0, 480);
                                }
                            }
                        }
                        parts.Add(new ListofResult() { FileName = title, Content = content, URL = url });
                        var endSearchTime = DateTime.Now.TimeOfDay;
                        var timeTaken = endSearchTime.TotalMilliseconds - startSearchTime.TotalMilliseconds;
                        totaltimeTakenToSearch = timeTaken.ToString();

                    }
                    //Search completed, dispose IndexSearcher
                    indexSearch.Dispose();
                    //assigning list into ViewBag
                    ViewBag.SearchResult = parts;
                }
                catch (Exception ex)
                {

                }
            }
            else
            {
                return RedirectToAction("UploadFile", "Home");
            }
            return View();
        }

        public ActionResult UploadFile()
        {
            return View();
        }

        [HttpPost]
        public ActionResult UploadFile(HttpPostedFileBase uploadDocument, string submitButton)
        {
            if (uploadDocument != null)
            {
                string docSavePath = Server.MapPath("/Documents/") + uploadDocument.FileName;
                uploadDocument.SaveAs(docSavePath);
                //Converting pdf and docx file into txt                            
                string fileLocation = Server.MapPath("/Documents");
                textConverter textConverter = new textConverter(fileLocation, docSavePath);
                textConverter.pdfConverted(); 
                textConverter.docxConverted();
                //end converting
                //Start indexing
                string txtPath = Server.MapPath("/temp");
                //string pagePath = Server.MapPath("/");
                string indexPath = Server.MapPath("/Index-lucene");
                DirectoryInfo dataInfo = new DirectoryInfo(txtPath);
                //DirectoryInfo dataInfo1 = new DirectoryInfo(pagePath);
                DirectoryInfo indexInfo = new DirectoryInfo(indexPath);
                //저장 방식
                //디렉토리 open
                //파일 시스템에 색인 파일을 저장하는 디렉토리 구현의 기본 클래스
                Lucene.Net.Store.Directory indexDir = FSDirectory.Open(
                                                                 indexInfo, _LockFactory);
                //Delete previous index first
                DeleteIndex(indexInfo, indexPath);
                //Generate new index
                var numIndexed = FileIndex(indexDir, dataInfo);
                //end indexing
                ViewBag.numIndexed += "Number of file indexed: " + numIndexed;
            }
            return View();
        }

        private void DeleteIndex(DirectoryInfo indexInfo, string indexPath)
        {
            try
            {
                var paths = indexInfo.EnumerateFiles("*");
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        string file = path.ToString();
                        string filepath = Path.Combine(indexPath, file);
                        System.IO.File.Delete(filepath);
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        private static int FileIndex(Lucene.Net.Store.Directory indexDir, DirectoryInfo dataInfo)
        {
            Analyzer anlalyzer = new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
            var writer = new IndexWriter(indexDir, anlalyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
            writer.SetMergePolicy(new LogDocMergePolicy(writer));
            try
            {
                var paths = dataInfo.EnumerateFiles("*.txt", SearchOption.AllDirectories);
                foreach (var path in paths)
                {
                    if (path.Name != "path.txt")
                    {
                        IndexFile(writer, path);
                    }
                }
            }
            catch (Exception ex)
            {
                writer.Dispose();
                throw ex;
            }
            var numIndexed = writer.MaxDoc();
            writer.Dispose();
            //delete temp created file after indexing
            deleteFiles(dataInfo);
            return numIndexed;
        }

        private static void IndexFile(IndexWriter writer, FileInfo file)
        {
            if (!file.Exists)
            {
                return;
            }
            Lucene.Net.Documents.Document doc = new Lucene.Net.Documents.Document();
            string path = file.FullName;
            string folder = file.DirectoryName;
            //string final_folder = Regex.Replace(folder, "temp", " ");
            TextReader readFile = new StreamReader(path);
            string temp_fileName = file.Name;
            string fileName = Regex.Replace(temp_fileName, ".txt", "");
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string test_content = "";
            string check_extention = Path.GetExtension(temp_fileName);
            var content = System.IO.File.ReadAllText(path);
            var test_content1 = string.Empty;
            string metaContent = string.Empty;
            string temp_relativePath = string.Empty;
            string final_relativePath = string.Empty;

                //Assigning original path
                pathList pathlist = new pathList();
                string[] listofpath = pathlist.readPath();
                foreach (string pathlists in listofpath)
                {
                    if (pathlists != string.Empty)
                    {
                        DirectoryInfo indexInfo = new DirectoryInfo(pathlists);
                        string fileNameOnly = indexInfo.Name;
                        if (fileName == fileNameOnly)
                        {
                            relativePath relativepath = new relativePath(pathlists);
                            final_relativePath = relativepath.getRelativePath(folder);
                            break;
                        }
                    }
                }
                //
                test_content = test_content + " " + content;
            
            //doc.Add(new Field("contents", readFile));
            doc.Add(new Field("prevewContent", test_content, Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.YES));
            doc.Add(new Field("URL", final_relativePath, Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.YES));
            doc.Add(new Field("filename", fileName, Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.YES));
            doc.Add(new Field("fileNameWithoutExtension", fileNameWithoutExtension, Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.YES));
            doc.Add(new Field("metaTag", metaContent, Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.YES));

            writer.AddDocument(doc);
            writer.Optimize();
            writer.Flush(true, true, true);
        }

        private static void deleteFiles(DirectoryInfo dataInfo)
        {
            var filePath = dataInfo.GetFiles();
            foreach (var file in filePath)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                System.IO.File.Delete(file.FullName);
            }
        }
    }
}