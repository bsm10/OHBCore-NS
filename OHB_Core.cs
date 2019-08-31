using Newtonsoft.Json;
using OHBEditor.FtpClient;
using OHBEditor.Helpers;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace OHBEditor
{
    public static class OHB_Core
    {
        public static readonly XName yml_catalog = "yml_catalog";
        public static readonly XName shop = "shop";
        public static readonly XName offers = "offers";
        public static readonly XName categoryId = "categoryId";
        public static readonly XName category = "category";
        public static readonly XName categories = "categories";
        public static readonly XName groups = "groups";
        public static readonly XName root = "root";
        public static readonly XName id = "id";
        public static readonly XName parentId = "parentId";
        public static readonly XName date = "date";
        public static readonly XName url = "url";
        public static readonly XName available = "available";

        private static XElement shopTree;
        private static IEnumerable<XElement> ExcludesGoods;//Collection, который содержит исключения которые не надо импортировать

        static IProgress<string> _progress;
        static IProgress<string> _progress2;

        public static TreeView treeViewMaster;
        public static TreeNode MasterNode { get; set; }
        public static TreeNode ExcludesNode { get; set; }
        public static IList<TreeNode> TreeNodeDataShops
        {
            get
            {
                return GetTreeNodes(treeViewMaster).ToList();
            }
        }
        public static async Task Update(IProgress<string> progress, IProgress<string> progress2, TreeView treeView1, TreeView treeView2)
        {
            _progress = progress;
            _progress2 = progress2;
            try
            {
                await GetShopsAsync(progress, progress2, treeView1, treeView2);
                Files.SaveXml(Files.FolderOHB_Local + Files.FileOHB_Shop, shopTree);
                await UploadFileAsync(Files.FileOHB_Shop, progress);
            }
            catch (Exception ex)
            {
                _progress.Report(ex.ToString());
            }
        }
        public static async Task UploadFileAsync(string fileName, IProgress<string> progress)
        {
            _progress = progress;
            try
            {
                _progress.Report("Uploading " + fileName + " to ftp...");

                // Создаем объект подключения по FTP
                Client client = new Client("ftp://ftp.s51.freehost.com.ua/", "granitmar1_ohbed", "Va3NeMHzyY");
                
                string file = Path.Combine(Files.FolderOHB_Local, fileName);
                string newFileName = Path.GetFileNameWithoutExtension(file)
                                     + DateTime.Now.ToString("dd-MM-yyyy HH:mm") + Path.GetExtension(file);
                _progress.Report(await client.RenameAsync(fileName, newFileName));
                _progress.Report(await client.UploadFileAsync(Path.Combine(Files.FolderOHB_Local, fileName), 
                                                                "ftp://ftp.s51.freehost.com.ua/" + fileName));

                //using (WebClient client = new WebClient())
                //{
                //    client.Credentials = new NetworkCredential("granitmar1_ohbed", "Va3NeMHzyY");
                //    byte[] responseArray = await client.UploadFileTaskAsync("ftp://ftp.s51.freehost.com.ua/" + fileName,
                //                                                            "STOR",
                //                                                            Path.Combine(Files.FolderOHB_Local, fileName));

                //    _progress.Report(Encoding.Default.GetString(responseArray) == "" ? "Ok!" :
                //                     Encoding.Default.GetString(responseArray));
                //}
                _progress.Report("Ok!");
            }
            catch (Exception ex)
            {
                _progress.Report(ex.Message);
            }
            finally
            {
                _progress.Report("Файл не удалось передать на фтп.");
            }
        }
        public static async Task MakeRequestProm()
        {
            string ACCESS_TOKEN = "288e0cb78e83277d2258b5c46e40d7bdb0a3ff74";
            string uri = "https://my.prom.ua/api/v1/";
            string qry_products = "products/list?limit = 10000 & group_id = ";
            //string qry_groups = "groups/list?limit=1000";
            //"?limit = 10000 & group_id = 777"
            //string qrystr = "https://my.prom.ua/api/v1/groups/list?limit=1000";
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ACCESS_TOKEN);
                    string response = await client.GetStringAsync(uri + qry_products);
                    XmlDocument doc = JsonConvert.DeserializeXmlNode(response, "root");
                    XDocument xdoc = XDocument.Parse(doc.OuterXml);
                    foreach (XElement group in xdoc.Element(root).Elements(groups))
                    {
                        XElement idgroup = group.Element(id);
                        //lst_groups.Add(idgroup);
                        response = await client.GetStringAsync(uri + qry_products + idgroup.Value);
                    }
                }
                catch (HttpRequestException ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

        }
        public static async Task<string> RequestPost(string request_string)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    string str = "";
                    WebRequest request = WebRequest.Create("https://my.prom.ua/api/v1/");
                    request.Method = "POST";
                    //288e0cb78e83277d2258b5c46e40d7bdb0a3ff74
                    // преобразуем данные в массив байтов
                    byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(request_string);
                    // устанавливаем тип содержимого - параметр ContentType
                    request.ContentType = "application/x-www-form-urlencoded";
                    // Устанавливаем заголовок Content-Length запроса - свойство ContentLength
                    request.ContentLength = byteArray.Length;

                    //записываем данные в поток запроса
                    using (Stream dataStream = request.GetRequestStream())
                    {
                        dataStream.Write(byteArray, 0, byteArray.Length);
                    }

                    WebResponse response = await request.GetResponseAsync();
                    using (Stream stream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            str = reader.ReadToEnd();
                        }
                    }
                    response.Close();
                    return str;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                return "";
            });

        }
        public static async Task<string> RequestGet(string request)
        {
            return await Task.Run(async () =>
            {
                using (HttpClient client = new HttpClient())
                {
                    // Call asynchronous network methods in a try/catch block to handle exceptions
                    try
                    {
                        //"Authorization: Bearer 288e0cb78e83277d2258b5c46e40d7bdb0a3ff74"
                        HttpResponseMessage response = await client.GetAsync("https://my.prom.ua/api/v1/products/list?limit=5?");
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        // Above three lines can be replaced with new helper method below
                        // string responseBody = await client.GetStringAsync(uri);
                        //Status = request + "-  Ok!";
                        return responseBody;
                    }
                    catch (HttpRequestException e)
                    {
                        MessageBox.Show(e.Message);
                    }
                }
                return "";
            });

            // Create a New HttpClient object and dispose it when done, so the app doesn't leak resources
        }

        /// <summary>
        /// Загружает полностью магазин поставщика с указанного URL
        /// </summary>
        /// <param name="url_shop"></param>
        /// <returns></returns>
        private static TreeNode GetShopsForXMLAsync(string url_shop)
        {
            try
            {
                //***************************************************************
                //загружаем магазин
                Uri uri = new Uri(url_shop);
                _progress.Report(uri.Host + " загрузка...");
                XDocument xYMLCatalog = XDocument.Load(url_shop);
                xYMLCatalog.Save(Files.FolderOHB_Local + uri.Host + ".xml");
                //список категорий
                IEnumerable<XElement> xCategories = xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements();
                //список всех товаров
                IEnumerable<XElement> allGoods = xYMLCatalog.Element(yml_catalog).Element(shop).Element(offers).Elements();
                //список товаров с учетом исключений | Без .ToArray() - очень долго считает
                IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new GoodsComparer()).ToArray();
                //добавляем Категории в общее дерево
                shopTree.Element(shop).Element(categories).Add(xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements());
                //добавляем Товары в общее дерево 
                shopTree.Element(shop).Element(offers).Add(ohbGoods);

                _progress.Report("категорий - " + xCategories.Count() + "; товаров в выгрузке - " + allGoods.Count()
                    + "; товаров с учетом исключений - " + ohbGoods.Count() + "; обновлено: " + xYMLCatalog.Element(yml_catalog).Attribute(date).Value);
                XAttribute xCatalogAttribute = xYMLCatalog.Element(yml_catalog).Attribute(date);
                DateTime lastUpdate = DateTime.Parse(xCatalogAttribute.Value);//дата последнего обновления

                //************************  строим дерево категорий-подкатегорий  ******************************
                //Добавляем магазин в TreeView
                TreeNode rootCatalog = new TreeNode(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url).Value +
                                        " - " + lastUpdate.ToString() + " (" + xCategories.Count() + ")");
                //Строим дерево
                RebuildTree(rootCatalog, ohbGoods);
                rootCatalog.Tag = xYMLCatalog.Element(yml_catalog);

                MasterNode.Nodes.Add(rootCatalog);

                return rootCatalog;
            }
            catch (XmlException xmlEx)
            {
                _progress.Report(xmlEx.Message);
            }
            catch (Exception ex)
            {
                _progress.Report(ex.Message);
            }
            return null;

        }

        /// <summary>
        /// Основная процедура, которая формирует итоговый файл с учетом исключений
        /// Заполняет также TreeView
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="progress2"></param>
        /// <param name="treeViewMaster1"></param>
        /// <param name="treeViewExcludes"></param>
        /// <returns></returns>
        public static async Task GetShopsAsync(IProgress<string> progress,
                                                IProgress<string> progress2,
                                                TreeView treeViewMaster1,
                                                TreeView treeViewExcludes
                                                )
        {
            try
            {
                _progress = progress;
                _progress2 = progress2;
                treeViewMaster = treeViewMaster1;
                //загружаем список магазинов
                XDocument xdoc = XDocument.Load(Files.FolderOHB_Remote + Files.FileOHB_ListShops);

                string time_update = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                shopTree =
                    new XElement(yml_catalog, new XAttribute(date, time_update),
                        new XElement(shop,
                            new XElement(categories),
                            new XElement(offers)));

                MasterNode = new TreeNode("One Home Beauty - " + time_update);

                //загружаем список исключений - товары, которые исключаются из общего файла
                //должно загружаться здесь
                ExcludesGoods = await LoadShopExcludesAsync();

                int count = xdoc.Element("shops-yml").Elements().Count();
                Task<TreeNode>[] task = new Task<TreeNode>[count];

                int i = 0;
                foreach (XElement addresxml in xdoc.Element("shops-yml").Descendants()) //XElement addresxml in xdoc.Element("shops-yml").Descendants()
                {
                    task[i] = new Task<TreeNode>(() => GetShopsForXMLAsync(addresxml.Value));
                    task[i].Start();
                    i++;
                }
                await Task.WhenAll(task); // ожидаем завершения задач 

                #region Заполняем TreeView с магазинами
                MasterNode.Tag = shopTree;
                treeViewMaster.Nodes.Add(MasterNode);
                treeViewMaster.Nodes[0].Expand();//раскрываем корневой
                #endregion

                #region Заполняем TreeView исключений
                TreeNode tnExcludes = new TreeNode("Исключения");
                ExcludesNode = RebuildTree(tnExcludes, ExcludesGoods);
                //ExcludesNode = LoadExcludesInTreeView();
                ExcludesNode.Tag = new XElement("Excludes");
                treeViewExcludes.Nodes.Add(ExcludesNode);
                treeViewExcludes.Nodes[0].Expand();//раскрываем корневой
                #endregion

                shopTree.Save(Files.FolderOHB_Local + Files.FileOHB_Shop);//сохраняем локальный файл

                Report(time_update);
            }
            catch (Exception e)
            {
                _progress.Report("GetShopsAsync - " + e.Message);
            }

        }

        private static void Report(string time_update)
        {
            _progress.Report("Всего категорий - " + shopTree.Element(shop).Element(categories).Elements().Count()
                           + "; товаров - " + shopTree.Element(shop).Element(offers).Elements().Count());
            FileInfo f = new FileInfo(Files.FolderOHB_Local + Files.FileOHB_Shop);
            _progress.Report("Локальный файл - " + f.FullName + "\r\n" + Math.Round((double)f.Length / 1000000, 2) + " Mb");
            _progress.Report("Время локального обновления - " + time_update);
        }
        class GoodsComparer : IEqualityComparer<XElement>
        {
            // Products are equal if their names and product categories are equal.
            public bool Equals(XElement x, XElement y)
            {
                return x.Attribute("id").Value == y.Attribute("id").Value && x.Element("categoryId").Value == y.Element("categoryId").Value;
            }

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(XElement product)
            {
                //Check whether the object is null
                if (object.ReferenceEquals(product, null)) return 0;

                //Get hash code for the Name field if it is not null.
                int hashProductName = product.Value == null ? 0 : product.Value.GetHashCode();

                //Get hash code for the Code field.
                int hashProductCode = product.Value.GetHashCode();

                //Calculate the hash code for the product.
                return hashProductName ^ hashProductCode;
            }

        }
        private static async Task<IEnumerable<XElement>> LoadShopExcludesAsync()
        {
            try
            {
                //***************************************************************
                //загружаем магазин
                XDocument xDoc;
                using (var httpclient = new HttpClient())
                {
                    var response = await httpclient.GetAsync(Path.Combine(Files.FolderOHB_Remote, Files.FileOHB_Excludes));  
                    xDoc = XDocument.Load(await response.Content.ReadAsStreamAsync());
                }
                return xDoc.Element("Excludes").Elements();
            }
            catch (XmlException xmlEx)
            {
                _progress.Report(xmlEx.Message);
            }
            catch (Exception ex)
            {
                _progress.Report(ex.Message);
            }
            return null;
        }
        private static void FindSubcategories(XElement categoryElement, TreeNode tnode, IEnumerable<XElement> categories)
        {
            tnode.ForeColor = Color.DarkSlateBlue;
            //tnode.NodeFont = new Font("Trebuchet MS", 13);
            IEnumerable<XElement> subcategories = from XElement subcat in categories
                                                  where subcat.Attributes(parentId).Count() > 0 &&
                                                        subcat.Attribute(parentId).Value == categoryElement.Attribute(id).Value
                                                  select subcat;

            foreach (XElement x in subcategories)
            {
                string idcategory = x.Attribute(id).Value;
                TreeNode tn = tnode.Nodes.Add(idcategory, x.Value);
                tn.Tag = x;
                FindSubcategories(x, tn, categories);
            }
        }
        private static void AddGoods(TreeNode treeNode, IEnumerable<XElement> xOffers)
        {
            XElement el = (XElement)treeNode.Tag;
            //if (treeNode.Nodes.Count == 0)
            if (treeNode.Nodes.Count == 0)
            {
                GetOffers(xOffers, treeNode);
            }
            else
            {
                foreach (TreeNode tn in treeNode.Nodes)
                {
                    GetOffers(xOffers, tn);//в категории с субкатегорией, могут тоже быть товары
                    AddGoods(tn, xOffers);
                }
            }
        }
        private static bool GetOffers(IEnumerable<XElement> xOffers, TreeNode tnCategory)
        {
            XElement gd = (XElement)tnCategory.Tag;
            IEnumerable<XElement> goods = from XElement offer in xOffers
                                          where offer.Element(categoryId).Value == gd.Attribute("id").Value
                                          select offer;

            if (goods.Count() == 0) return false;

            foreach (XElement g in goods)
            {
                TreeNode trnOffer = new TreeNode(g.Element("name").Value);
                trnOffer.Tag = g;
                tnCategory.Nodes.Add(trnOffer);
            }

            return true;
        }
        public static async Task SaveExcludes(TreeNodeCollection treeNodeCollection)
        {
            await Task.Run(() =>
            {
                {
                    XElement excludes = new XElement("Excludes", new XAttribute("date", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));

                    foreach (TreeNode tn in treeNodeCollection)
                    {
                        GetExcludesGoods(excludes, tn);
                    }
                    FileInfo fileInfoLocalEcxludes;
                    Files.SaveXml(Files.FolderOHB_Local + Files.FileOHB_Excludes, excludes, out fileInfoLocalEcxludes);
                    _progress.Report("Добавлено в исключения - " + excludes.Elements().Count() + " наименований");
                    _progress2.Report(fileInfoLocalEcxludes.FullName);

                }
            });

        }
        public static TreeNode RebuildTree(TreeNode tnRoot, IEnumerable<XElement> goods)
        {
            try
            {
                IEnumerable<XElement> _categories = shopTree.Element(shop).Element(categories).Elements();
                //берем корневые категории - которые не имеют parentId или parentId == 0
                foreach (XElement rootCategory in _categories.Where(e => e.Attributes(parentId).Count() == 0
                                                                      || e.Attribute(parentId).Value == "0"))
                {
                    //tnRoot.NodeFont = new Font("Trebuchet MS", 12);
                    tnRoot.ForeColor = Color.DarkMagenta;

                    TreeNode tn = tnRoot.Nodes.Add(rootCategory.Attribute(id).Value, rootCategory.Value);
                    tn.Tag = rootCategory;
                    //заполняем корневые категории подкатегориями
                    FindSubcategories(rootCategory, tnRoot.Nodes[rootCategory.Attribute(id).Value], _categories);
                }
                //заполняем все категории товарами
                AddGoods(tnRoot, goods);
                RemoveEmptyCategory(tnRoot);
                return tnRoot;
            }
            catch (Exception e)
            {
                _progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
                return null;
            }
        }
        public static TreeNode RebuildTree(TreeNode tnRoot)
        {
            try
            {
                IEnumerable<XElement> _categories = GetXElementsFromTreeNode(tnRoot, "category");

                IEnumerable<XElement> _goods = GetXElementsFromTreeNode(tnRoot, "item")
                                                    .Concat(GetXElementsFromTreeNode(tnRoot, "offer"));

                //берем корневые категории - которые не имеют parentId или parentId == 0
                foreach (XElement rootCategory in _categories.Where(e => e.Attributes(parentId).Count() == 0
                                                                      || e.Attribute(parentId).Value == "0"))
                {
                    //tnRoot.NodeFont = new Font("Trebuchet MS", 12);
                    tnRoot.ForeColor = Color.DarkMagenta;

                    TreeNode tn = tnRoot.Nodes.Add(rootCategory.Attribute(id).Value, rootCategory.Value);
                    tn.Tag = rootCategory;
                    //заполняем корневые категории подкатегориями
                    FindSubcategories(rootCategory, tnRoot.Nodes[rootCategory.Attribute(id).Value], _categories);
                }
                //заполняем все категории товарами
                AddGoods(tnRoot, _goods);
                RemoveEmptyCategory(tnRoot);
                return tnRoot;
            }
            catch (Exception e)
            {
                _progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
                return null;
            }
        }
        private static void RemoveEmptyCategory(TreeNode node)
        {
            IEnumerable<TreeNode> res = new[] { node }.Concat(node.Nodes
                                .OfType<TreeNode>()
                                .SelectMany(x => GetNodeAndChildren(x)))
                                .Where(x => ((XElement)x.Tag)?.Name == "category" & x.Nodes.Count == 0);
            while (res.Count() != 0)
            {
                res.FirstOrDefault().Remove();
            }
        }
        private static void GetExcludesGoods(XElement excludes, TreeNode treeNode)
        {
            XElement excludeItem = (XElement)treeNode.Tag;
            if (excludeItem.Name == "item" || excludeItem.Name == "offer")
            {
                excludes.Add(excludeItem);
            }
            else
            {
                foreach (TreeNode tn in treeNode.Nodes)
                {
                    GetExcludesGoods(excludes, tn);
                }
            }

        }
        public static TreeNode[] GetTreeNode(string seachString, TreeView treeView)
        {
            if (seachString == "")
            {
                return GetTreeNodes(treeView);
            }
            TreeNode[] treeNodes = treeView.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             .Where(r => r.Text.ToUpper().Contains(seachString.ToUpper()))
             .ToArray();
            return treeNodes;
        }
        public static TreeNode[] GetTreeNodes(TreeView treeView)
        {
            TreeNode[] treeNodes = treeView.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             .ToArray();
            return treeNodes;
        }
        private static IEnumerable<TreeNode> GetNodeAndChildren(TreeNode node)
        {
            return new[] { node }.Concat(node.Nodes
                                            .OfType<TreeNode>()
                                            .SelectMany(x => GetNodeAndChildren(x)));
        }
        public static void GetCountOfItemsInTreeView(TreeNodeCollection tnc, ref int i)
        {
            foreach (TreeNode node in tnc)
            {
                XElement el = (XElement)node.Tag;
                if (el.Name != "category")
                {
                    i++;
                }
                else
                {
                    GetCountOfItemsInTreeView(node.Nodes, ref i);
                }

            }
        }
        public static int GetCountOfItemsInTreeView(TreeNode node)
        {
            IEnumerable<TreeNode> res = new[] { node }.Concat(node.Nodes
                                            .OfType<TreeNode>()
                                            .Where(n => ((XElement)n.Tag).Name != "category")
                                            .SelectMany(x => GetNodeAndChildren(x)));

            return res.Count();
        }
        public static IEnumerable<XElement> GetXElementsFromTreeNode(TreeNode node, string name)
        {
            IEnumerable<TreeNode> res = new[] { node }.Concat(node.Nodes
                    .OfType<TreeNode>()
                    .SelectMany(x => GetNodeAndChildren(x)))
                    .Where(x => ((XElement)x.Tag)?.Name == name); //"category"

            List<XElement> xres = new List<XElement>();
            foreach (TreeNode treeNode in res)
                xres.Add((XElement)treeNode.Tag);

            return xres;

            //while (res.Count() != 0)
            //{
            //    res.FirstOrDefault().Remove();
            //}

        }
    }
    namespace Helpers
    {
        public static class Files
        {
            public static string FolderOHB_Local { get; } = Application.UserAppDataPath + @"\";
            public static string FolderOHB_Remote { get; } = @"http://onebeauty.com.ua/files/";
            public static string FileOHB_Logfile { get; set; }
            public static string FileOHB_Shop { get; } = "onehomebeauty.xml";
            public static string FileOHB_Excludes { get; } = "excludes.xml";
            public static string FileOHB_ListShops { get; } = "shops-yml.xml";
            public static async Task SaveXMLShops(string fileName, string ymlCatalog, IProgress<string> progress)
            {
                try
                {
                    progress.Report("Сохраняю файл - " + fileName);
                    string file = FolderOHB_Local + fileName; //+ uri.Host + ".xml";
                    using (StreamWriter sw = File.CreateText(file))
                    {
                        await sw.WriteAsync(ymlCatalog);
                    }
                    FileInfo fi = new FileInfo(file);
                    progress.Report("Ок - " + fi.Length);
                }
                catch (XmlException xmlEx)
                {
                    progress.Report(xmlEx.Message);
                }
                catch (Exception ex)
                {
                    progress.Report(ex.Message);
                }
            }

            public static void SaveXml(string file, XElement xEl)
            {
                if (string.IsNullOrEmpty(file))
                {
                    throw new ArgumentException("message", nameof(file));
                }

                using (StreamWriter sw = File.CreateText(file))
                {
                    xEl.Save(sw);
                }
            }
            public static void SaveXml(string file, XElement xEl, out FileInfo fileInfo)
            {
                if (string.IsNullOrEmpty(file))
                {
                    throw new ArgumentException("message", nameof(file));
                }

                using (StreamWriter sw = File.CreateText(file))
                {
                    xEl.Save(sw);
                    fileInfo = new FileInfo(file);
                }
            }
            public static async Task<XDocument> LoadXMLAsync(string path)
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        return XDocument.Load(path);
                    }
                    catch (FileNotFoundException)
                    {
                        return null;
                    }
                    catch (XmlException)
                    {
                        return null;
                    }

                });

            }
            public static async Task<bool> CheckVersionsOfFilesAsync(string file, IProgress<string> progress)
            {
                XDocument loc = await LoadXMLAsync(FolderOHB_Local + file);
                XDocument rem = await LoadXMLAsync(FolderOHB_Remote + file);


                bool result = (loc?.Root.Attribute("date").Value == rem?.Root.Attribute("date").Value);
                //bool result = loc.Root.GetHashCode().Equals(rem.Root.GetHashCode());

                progress.Report("Локальный " + file + " - " + loc?.Root.Attribute("date").Value + "\r\n" +
                                "На сервере " + file + " - " + rem?.Root.Attribute("date").Value);
                return result;
            }

        }
    }

    namespace FtpClient
    {
        public class Client
        {
            private string password;
            private string userName;
            private string uri;
            private int bufferSize = 1024;

            public bool Passive = true;
            public bool Binary = true;
            public bool EnableSsl = false;
            public bool Hash = false;

            public Client(string uri, string userName, string password)
            {
                this.uri = uri;
                this.userName = userName;
                this.password = password;
            }

            public string ChangeWorkingDirectory(string path)
            {
                uri = combine(uri, path);

                return PrintWorkingDirectory();
            }

            public string DeleteFile(string fileName)
            {
                var request = createRequest(combine(uri, fileName), WebRequestMethods.Ftp.DeleteFile);

                return getStatusDescription(request);
            }

            public string DownloadFile(string source, string dest)
            {
                var request = createRequest(combine(uri, source), WebRequestMethods.Ftp.DownloadFile);

                byte[] buffer = new byte[bufferSize];

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    using (var stream = response.GetResponseStream())
                    {
                        using (var fs = new FileStream(dest, FileMode.OpenOrCreate))
                        {
                            int readCount = stream.Read(buffer, 0, bufferSize);

                            while (readCount > 0)
                            {
                                if (Hash)
                                    Console.Write("#");

                                fs.Write(buffer, 0, readCount);
                                readCount = stream.Read(buffer, 0, bufferSize);
                            }
                        }
                    }

                    return response.StatusDescription;
                }
            }

            public DateTime GetDateTimestamp(string fileName)
            {
                var request = createRequest(combine(uri, fileName), WebRequestMethods.Ftp.GetDateTimestamp);

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    return response.LastModified;
                }
            }

            public long GetFileSize(string fileName)
            {
                var request = createRequest(combine(uri, fileName), WebRequestMethods.Ftp.GetFileSize);

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    return response.ContentLength;
                }
            }

            public string[] ListDirectory()
            {
                var list = new List<string>();

                var request = createRequest(WebRequestMethods.Ftp.ListDirectory);

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    using (var stream = response.GetResponseStream())
                    {
                        using (var reader = new StreamReader(stream, true))
                        {
                            while (!reader.EndOfStream)
                            {
                                list.Add(reader.ReadLine());
                            }
                        }
                    }
                }

                return list.ToArray();
            }

            public string[] ListDirectoryDetails()
            {
                var list = new List<string>();

                var request = createRequest(WebRequestMethods.Ftp.ListDirectoryDetails);

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    using (var stream = response.GetResponseStream())
                    {
                        using (var reader = new StreamReader(stream, true))
                        {
                            while (!reader.EndOfStream)
                            {
                                list.Add(reader.ReadLine());
                            }
                        }
                    }
                }

                return list.ToArray();
            }

            public string MakeDirectory(string directoryName)
            {
                var request = createRequest(combine(uri, directoryName), WebRequestMethods.Ftp.MakeDirectory);

                return getStatusDescription(request);
            }

            public string PrintWorkingDirectory()
            {
                var request = createRequest(WebRequestMethods.Ftp.PrintWorkingDirectory);

                return getStatusDescription(request);
            }

            public string RemoveDirectory(string directoryName)
            {
                var request = createRequest(combine(uri, directoryName), WebRequestMethods.Ftp.RemoveDirectory);

                return getStatusDescription(request);
            }

            public async Task<string> RenameAsync(string currentName, string newName)
            {
                return await Task.Run(() =>
                {
                    var request = createRequest(combine(uri, currentName), WebRequestMethods.Ftp.Rename);
                    request.RenameTo = newName;
                    return getStatusDescription(request);
                });
            }

            public async Task<string> UploadFileAsync(string source, string destination)
            {
                return await Task.Run(() =>
                {
                    var request = createRequest(destination, WebRequestMethods.Ftp.UploadFile);
                    //var request = createRequest(combine(uri, destination), WebRequestMethods.Ftp.UploadFile);

                    using (var stream = request.GetRequestStream())
                    {
                        using (var fileStream = System.IO.File.Open(source, FileMode.Open))
                        {
                            int num;

                            byte[] buffer = new byte[bufferSize];

                            while ((num = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (Hash)
                                    Console.Write("#");

                                stream.Write(buffer, 0, num);
                            }
                        }
                    }
                    return getStatusDescription(request);
                });
            }

            public string UploadFileWithUniqueName(string source)
            {
                var request = createRequest(WebRequestMethods.Ftp.UploadFileWithUniqueName);

                using (var stream = request.GetRequestStream())
                {
                    using (var fileStream = System.IO.File.Open(source, FileMode.Open))
                    {
                        int num;

                        byte[] buffer = new byte[bufferSize];

                        while ((num = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (Hash)
                                Console.Write("#");

                            stream.Write(buffer, 0, num);
                        }
                    }
                }

                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    return Path.GetFileName(response.ResponseUri.ToString());
                }
            }

            private FtpWebRequest createRequest(string method)
            {
                return createRequest(uri, method);
            }

            private FtpWebRequest createRequest(string uri, string method)
            {
                var r = (FtpWebRequest)WebRequest.Create(uri);

                r.Credentials = new NetworkCredential(userName, password);
                r.Method = method;
                r.UseBinary = Binary;
                r.EnableSsl = EnableSsl;
                r.UsePassive = Passive;

                return r;
            }

            private string getStatusDescription(FtpWebRequest request)
            {
                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    return response.StatusDescription;
                }
            }

            private string combine(string path1, string path2)
            {
                return Path.Combine(path1, path2).Replace("\\", "/");
            }
        }

    }

}
