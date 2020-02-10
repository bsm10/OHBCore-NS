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
using static OHBEditor.Helpers.Files;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections;

namespace OHBEditor
{
    public static class OHB_Core
    {
        #region Constants
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
        public static readonly XName name = "name";
        public static readonly XName vendor = "vendor";
        public static readonly XName vendorCode = "vendorCode";
        #endregion

        private static XElement shopTree; //общее дерево магазина OHB
        private static XElement shopTreeNewGoods; //дерево новых товаров

        public static IEnumerable<XElement> ExcludesGoods { get; set; }//Collection, который содержит исключения которые не надо импортировать
        //private static XElement xExcludesGoods; //xElement, который содержит Categories и исключения
        private static IEnumerable<XElement> NewGoods { get; set; }

        public static IProgress<string> progress;
        public static TreeView treeViewMaster { get; set; }
        public static TreeView treeViewExcludes { get; set; }
        public static TreeView treeViewNewOffers { get; set; }
        public static TreeNode MasterNode { get; set; }
        public static List<TreeNode> ListShopNodes { get; set; } //list for adding node of shop 
        public static TreeNode ExcludesNode { get; set; }
        public static TreeNode NewOffersNode { get; set; }
        public static IList<TreeNode> TreeNodeDataShops
        {
            get
            {
                return GetTreeNodes(treeViewMaster).ToList();
            }
        }

        public static BindingList<string> listShops;//список выгрузок
        
        public static XDocument xdocListShops { get; set; } //список url выгрузок
        public static List<XDocument> ListShops { get; set; } //список выгрузок
        public static XDocument xdocRemOHBShop { get; set; } //магазин на сервере
        public static XDocument xdocRemExcludes { get; set; } //список исключений на сервере
        public static XDocument xdocLocOHBShop { get; set; } //магазин local
        public static XDocument xdocLocExcludes { get; set; } //список исключений на сервере local

        public static async Task InitializationAsync(IProgress<string> _progress, 
                                                    TreeView treeView1, 
                                                    TreeView treeView2, 
                                                    TreeView treeViewNew, 
                                                    bool loadXML = true)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.DoWork += new DoWorkEventHandler(backgroundWorker1_DoWork);
            backgroundWorker1.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker1_RunWorkerCompleted);
            Debug.AutoFlush = true;
            progress = _progress;
            treeViewMaster = treeView1;
            treeViewExcludes = treeView2;
            treeViewNewOffers = treeViewNew;

            //Главная загрузка всех xml
            if (loadXML) await LoadXMLAsync();
            //запуск задачи построения дерева исключений
            backgroundWorker1.RunWorkerAsync();
            //построить дерево товаров
            await GetShopsAsync();
            sw.Stop();
            progress.Report($"General time loading - {sw.Elapsed}");
            _ = GetShopStatisticServerAsync(progress);
        }

        private static void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                treeViewExcludes.BeginUpdate();
                treeViewExcludes.Nodes.Add(ExcludesNode);
                treeViewExcludes.Nodes[0].Expand();//раскрываем корневой
                treeViewExcludes.EndUpdate();
            }
            catch(Exception ex)
            {
                progress.Report($"backgroundWorker1_RunWorkerCompleted: {ex.Message}");
            }
        }

        private static void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            AddExcludesInTreeView(buildTree: true);
        }

        public static void AddExcludesInTreeView(bool buildTree = false)
        {
            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                #region Заполняем TreeView исключений
                TreeNode tnExcludes = new TreeNode("Исключения");
                progress.Report("Заполняем дерево исключений...");
                IEnumerable<XElement> e = xdocRemOHBShop.Element(yml_catalog).Element(shop).Element(categories).Elements().ToArray();
                if(buildTree) ExcludesNode = RebuildTree(tnExcludes, e, ExcludesGoods);
                if (ExcludesNode == null)
                {
                    ExcludesNode = new TreeNode("Исключения");
                }
                ExcludesNode.Tag = new XElement("Excludes");
                sw.Stop();
                progress.Report($"Дерево исключений - построено! {sw.Elapsed}");
                #endregion
            }
            catch(Exception e)
            {
                progress.Report($"AddExcludesInTreeView() - {e.Message}");
            }
        }

        public static async Task UploadFileAsync(string fileName)
        {
            try
            {
                progress.Report("Uploading " + fileName + " to ftp...");

                // Создаем объект подключения по FTP
                Client client = new Client("ftp://ftp.s51.freehost.com.ua/", "granitmar1", "96OVL4PmL8");
                
                string file = Path.Combine(FolderOHB_Local, fileName);
                //string newFileName = Path.GetFileNameWithoutExtension(file)
                //                     + DateTime.Now.ToString("dd-MM-yyyy HH:mm") + Path.GetExtension(file);
                string newFileName = Path.GetFileNameWithoutExtension(file)
                                     + "2" + Path.GetExtension(file);
                client.ChangeWorkingDirectory("www.onebeauty.com.ua/files/");
                if (client.ListDirectory().Where(f => f == fileName).Count() > 0)
                    progress.Report(await client.RenameAsync(fileName, newFileName));

                progress.Report(await client.UploadFileAsync(Path.Combine(FolderOHB_Local, fileName),
                                                                "ftp://ftp.s51.freehost.com.ua/www.onebeauty.com.ua/files/" + fileName));
                progress.Report("•————————————————• Ok! •—————————————————•");
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }
        }
        public static async Task MakeRequestProm()
        {
            string ACCESS_TOKEN = "288e0cb78e83277d2258b5c46e40d7bdb0a3ff74";
            string uri = "https://my.prom.ua/api/v1/";
            string qry_products = "products/{id}";
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
        private static async Task<string> RequestGet(string request)
        {
            return await Task.Run(async () =>
            {
                using (HttpClient client = new HttpClient())
                {
                    // Call asynchronous network methods in a try/catch block to handle exceptions
                    try
                    {
                        //"Authorization: Bearer 288e0cb78e83277d2258b5c46e40d7bdb0a3ff74"
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "288e0cb78e83277d2258b5c46e40d7bdb0a3ff74");
                        HttpResponseMessage response = await client.GetAsync(request);
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        // Above three lines can be replaced with new helper method below
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

        public static async Task PromGetProductInfo(string id)
        {
            string uri = $"https://my.prom.ua/api/v1/products/{id}";
            await Task.Run(async () =>
            {
                string result = await RequestGet(uri);
            });

        }

        /// <summary>
        /// Загружает полностью магазин поставщика с указанного URL
        /// </summary>
        /// <param name="url_shop"></param>
        /// <returns></returns>
        private static void GetShopsForXML(XDocument xYMLCatalog)
        {
            try
            {
                Stopwatch sw = new Stopwatch();
                Stopwatch sw1 = new Stopwatch();
                sw1.Start();
                //***************************************************************
                Uri uri = new Uri(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url)?.Value);
                progress.Report(uri.OriginalString + " building tree...");
                
                string nameShop = uri.Host;
                xYMLCatalog.Save(nameShop + ".xml");
                //список категорий
                IEnumerable<XElement> xCategories = xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements();
                //список всех товаров в загрузке
                IEnumerable<XElement> allGoods = xYMLCatalog.Element(yml_catalog).Element(shop).Element(offers).Elements();
                sw.Start();
                //список товаров с учетом исключений | Без .ToArray() - очень долго считает
                IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods.AsParallel().ToArray(), new OfferComparer()).ToArray();
                //отсеиваим новые
                IEnumerable<XElement> ohbGoodsServer = ohbGoods.Intersect(xdocRemOHBShop.Element(yml_catalog).Element(shop).Element(offers).Elements(), new OfferComparer()).ToArray();
                //новые добавляем
                IEnumerable<XElement> ohbGoodsNew = ohbGoods.Except(ohbGoodsServer.AsParallel(), new OfferComparer()).ToArray();
                sw.Stop();
                Debug.WriteLine($"{nameShop} load: {allGoods.Count()}, on server: {ohbGoodsServer.Count()}, local: {ohbGoods.Count()}, exclude: {allGoods.Count() - ohbGoods.Count()}, new: {ohbGoodsNew.Count()} - elapsed: {sw.ElapsedMilliseconds}");

                //добавляем Категории в общее дерево
                shopTree.Element(shop).Element(categories).Add(xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements());
                //добавляем Товары в общее дерево 
                shopTree.Element(shop).Element(offers).Add(ohbGoodsServer);//добавляем только те, что есть на сервере
                //добавляем новые товары в отдельный х-элемент
                if(ohbGoodsNew.Count() > 0) 
                    shopTreeNewGoods.Add(ohbGoodsNew);

                XAttribute xCatalogAttribute = xYMLCatalog.Element(yml_catalog).Attribute(date);
                //DateTime lastUpdate = DateTime.Parse(xCatalogAttribute.Value);//дата последнего обновления

                //************************  строим дерево категорий-подкатегорий  ******************************
                //Добавляем магазин в TreeView
                if(ohbGoodsServer.Count() > 0)
                {
                    TreeNode rootCatalog = new TreeNode($"{nameShop}");
                    //Строим дерево
                    RebuildTree(rootCatalog, xCategories, ohbGoodsServer);
                    rootCatalog.Text += $" — {DateTime.Parse(xCatalogAttribute.Value).ToString()}";
                    rootCatalog.Tag = xYMLCatalog.Element(yml_catalog);
                    MasterNode.Nodes.Add(rootCatalog);
                }

                //new goods
                //shopTreeNewGoods
                int count = ohbGoodsNew.Count();
                if(count > 0)
                {
                    TreeNode tnNewOffers = new TreeNode($"{nameShop} - {count}");
                    RebuildTree(tnNewOffers, shopTree.Element(shop).Element(categories).Elements(), ohbGoodsNew);
                    NewOffersNode.Nodes.Add(tnNewOffers);
                }
                sw1.Stop();
                progress.Report($"{nameShop} Ok!: категорий - " + xCategories.Count() + 
                                ";\r\nтоваров в выгрузке - " + allGoods.Count() +
                                ";\r\nтоваров с учетом исключений - " + ohbGoods.Count() +
                                ";\r\nновых - " + count +
                                ";\r\nобновлено: " + xYMLCatalog.Element(yml_catalog).Attribute(date).Value +
                                $"\r\n————— Done! Elapsed time: {sw1.Elapsed} —————");

               
            }
            catch (XmlException xmlEx)
            {
                progress.Report($"GetShopsForXML: {xmlEx.Message}");
            }
            catch (Exception ex)
            {
                progress.Report($"GetShopsForXML: {ex.Message}");
            }
           

        }
        private static async Task<TreeNode> GetShopsForXMLAsync(string url_shop)
        {
            await Task.Run(() =>
            {
                try
                {
                    //***************************************************************
                    //загружаем магазин
                    Uri uri = new Uri(url_shop);
                    progress.Report(uri.OriginalString + " загрузка...");
                    XDocument xYMLCatalog = XDocument.Load(url_shop);
                    string nameShop = uri.Host + uri.PathAndQuery.Replace("/", "-");
                    xYMLCatalog.Save(nameShop + ".xml");
                    //список категорий
                    IEnumerable<XElement> xCategories = xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements();
                    //список всех товаров
                    IEnumerable<XElement> allGoods = xYMLCatalog.Element(yml_catalog).Element(shop).Element(offers).Elements();
                    //список товаров с учетом исключений | Без .ToArray() - очень долго считает
                    IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new OfferComparer()).ToArray();
                    //добавляем Категории в общее дерево
                    shopTree.Element(shop).Element(categories).Add(xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements());
                    //добавляем Товары в общее дерево 
                    shopTree.Element(shop).Element(offers).Add(ohbGoods);

                    XAttribute xCatalogAttribute = xYMLCatalog.Element(yml_catalog).Attribute(date);
                    //DateTime lastUpdate = DateTime.Parse(xCatalogAttribute.Value);//дата последнего обновления

                    //************************  строим дерево категорий-подкатегорий  ******************************
                    //Добавляем магазин в TreeView
                    //TreeNode rootCatalog = new TreeNode(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url).Value +
                    //                        " - " + lastUpdate.ToString() + " (" + xCategories.Count() + ")");
                    TreeNode rootCatalog = new TreeNode($"{nameShop}");
                    //Строим дерево
                    RebuildTree(rootCatalog, xCategories, ohbGoods);
                    rootCatalog.Tag = xYMLCatalog.Element(yml_catalog);

                    MasterNode.Nodes.Add(rootCatalog);

                    progress.Report($"{nameShop} готово!: категорий - " + xCategories.Count() + "; товаров в выгрузке - " + allGoods.Count()
                    + "; товаров с учетом исключений - " + ohbGoods.Count() + "; обновлено: " + xYMLCatalog.Element(yml_catalog).Attribute(date).Value);

                    return rootCatalog;
                }
                catch (XmlException xmlEx)
                {
                    progress.Report(xmlEx.Message);
                    return null;
                }
                catch (Exception ex)
                {
                    progress.Report(ex.Message);
                    return null;
                }
            });
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
        public static async Task GetShopsAsync()
        {
            try
            {
                //загружаем список магазинов
                //XDocument xdoc = XDocument.Load(Files.FolderOHB_Remote + Files.FileOHB_ListShops);

                string time_update = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                shopTree = new XElement(yml_catalog, new XAttribute(date, time_update),
                                       new XElement(shop,
                                       new XElement(categories),
                                       new XElement(offers)));

                shopTreeNewGoods = new XElement(offers);
                
                MasterNode = new TreeNode($"One Home Beauty - {time_update}");
                NewOffersNode = new TreeNode($"Новые поступления {time_update}");

                Task[] tasks = new Task[ListShops.Count()];
                int i = 0;
                foreach (XDocument shop in ListShops) // (string addresxml in listShops)
                {
                    tasks[i] = new Task(() => GetShopsForXML(shop), TaskCreationOptions.LongRunning);
                    tasks[i].Start();
                    i++;
                }
                await Task.WhenAll(tasks); // ожидаем завершения задач после чего будет доступна поная секция Categories

                #region Заполняем TreeView с магазинами
                MasterNode.Tag = shopTree;
                treeViewMaster.BeginUpdate();
                treeViewMaster.Nodes.Add(MasterNode);
                treeViewMaster.Nodes[0].Expand();//раскрываем корневой
                treeViewMaster.EndUpdate();
                #endregion

                shopTree.Save(FolderOHB_Local + FileOHB_Shop);//сохраняем локальный файл

                #region Заполняем TreeViewNew новыми поступлениями
                treeViewNewOffers.BeginUpdate();
                foreach (TreeNode tn in NewOffersNode.Nodes)
                {
                    treeViewNewOffers.Nodes.Add(tn);
                }
                treeViewNewOffers.EndUpdate();
                #endregion
                СommonReport(time_update);
            }
            catch (Exception e)
            {
                progress.Report("GetShopsAsync - " + e.Message);
            }

        }

        /// <summary>
        /// Извлечение всех узлов TreeView без рекурсии
        /// </summary>
        /// <param name="treeView"></param>
        /// <returns></returns>
        public static IEnumerable<TreeNode> AllTreeNodes(TreeView treeView)
        {
            Queue<TreeNode> nodes = new Queue<TreeNode>();
            foreach (TreeNode item in treeView.Nodes)
                nodes.Enqueue(item);

            while (nodes.Count > 0)
            {
                TreeNode node = nodes.Dequeue();
                yield return node;
                foreach (TreeNode item in node.Nodes)
                    nodes.Enqueue(item);
            }
        }

        public static IEnumerable<TreeNode> AllTreeNodes(TreeNodeCollection tnc)
        {
            Queue<TreeNode> nodes = new Queue<TreeNode>();
            foreach (TreeNode item in tnc)
                nodes.Enqueue(item);

            while (nodes.Count > 0)
            {
                TreeNode node = nodes.Dequeue();
                yield return node;
                foreach (TreeNode item in node.Nodes)
                    nodes.Enqueue(item);
            }
        }


        public static void СommonReport(string time_update)
        {
            //progress.Report("Всего категорий - " + shopTree.Element(shop).Element(categories).Elements().Count()
            //               + "; товаров - " + shopTree.Element(shop).Element(offers).Elements().Count());
            FileInfo f = new FileInfo(FolderOHB_Local + FileOHB_Shop);
            progress.Report("Локальный файл - " + f.FullName + "\r\n" + Math.Round((double)f.Length / 1000000, 2) + " Mb");
            progress.Report("Время локального обновления - " + time_update);
            
        }
        class OfferComparer : IEqualityComparer<XElement>
        {
            // Products are equal if their names and product categories are equal.
            public bool Equals(XElement x, XElement y)
            {
                bool eq;
                bool eqId = x.Attribute("id").Value == y.Attribute("id").Value;

                if (x.Element(vendorCode) != null && y.Element(vendorCode) != null)
                {
                    eq = x.Element(vendorCode).Value == y.Element(vendorCode).Value;
                }
                else if (x.Element(vendor) != null && y.Element(vendor) != null)
                {
                    eq = x.Element(vendor).Value == y.Element(vendor).Value;
                }
                else return eqId;

                return eqId && eq;

                //return x.Attribute("id").Value == y.Attribute("id").Value &&
                //    x.Element("name").Value == y.Element("name").Value;
                //&& x.Element("name").Value == y.Element("name").Value;
                //return x.Attribute("id").Value == y.Attribute("id").Value;

            }

            // then GetHashCode() must return the same value for these objects.
            public int GetHashCode(XElement product)
            {
                //Check whether the object is null
                if (object.ReferenceEquals(product, null)) return 0;
                //Get hash code for the Name field if it is not null.
                int hashProductId = product.Attribute(id).Value == null ? 0 : product.Attribute(id).Value.GetHashCode();
                int hashProductVendor;
                if (product.Element(vendorCode) != null)
                {
                    hashProductVendor = product.Element(vendorCode).Value == null ? 0 : product.Element(vendorCode).Value.GetHashCode();
                }
                else if (product.Element(vendor) != null)
                {
                    hashProductVendor = product.Element(vendor).Value == null ? 0 : product.Element(vendor).Value.GetHashCode();
                }
                else hashProductVendor = 1;
                //Calculate the hash code for the product.
                return hashProductId * hashProductVendor;
            }

        }

        /// <summary>
        /// Загружаем магазин и исключения с сервера и локально 
        /// </summary>
        private static async Task LoadXMLAsync(bool bDownloadShops = true)
        {
            try
            {
                progress.Report($"Загружаю xml...");
                Task[] tasks = new Task[5];
                //файл магазина на сервере
                tasks[0] = new Task<XDocument>(() => {
                    try
                    {
                        xdocRemOHBShop = XDocument.Load(FolderOHB_Remote + FileOHB_Shop);
                        return xdocRemOHBShop;
                    }
                    catch
                    {
                        return null;
                    }
                }, TaskCreationOptions.LongRunning);

                //файл исключений на сервере
                tasks[1] = new Task<XDocument>(() => {
                    try
                    {
                        xdocRemExcludes = XDocument.Load(FolderOHB_Remote + FileOHB_Excludes);
                    }
                    catch(WebException)
                    {
                        XElement excludes = new XElement("Excludes", new XAttribute("date", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
                        xdocRemExcludes = XDocument.Parse(excludes.ToString());
                    }
                    catch (Exception ex)
                    {
                        xdocRemExcludes = null;
                        progress.Report(ex.Message);
                    }
                    ExcludesGoods = xdocRemExcludes?.Element("Excludes").Elements().ToArray();
                    return xdocRemExcludes;
                }, TaskCreationOptions.LongRunning);

                //файл магазина локальный
                tasks[2] = new Task<XDocument>(() => {
                    try
                    {
                        xdocLocOHBShop = XDocument.Load(FolderOHB_Local + FileOHB_Shop);
                        return xdocLocOHBShop;
                    }
                    catch
                    {
                        return null;
                    }
                }, TaskCreationOptions.LongRunning);

                //файл исключений локальный
                tasks[3] = new Task<XDocument>(() => {
                    try
                    {
                        xdocLocExcludes = XDocument.Load(FolderOHB_Local + FileOHB_Excludes);
                        return xdocLocExcludes;
                    }
                    catch
                    {
                        return null;
                    }
                }, TaskCreationOptions.LongRunning);

                //список выгрузок на сервере и загрузка магазинов
                tasks[4] = new Task<XDocument>(() =>
                {
                    try
                    {
                        xdocListShops = XDocument.Load(FolderOHB_Remote + FileOHB_ListShops);
                        List<string> lst = (from e in xdocListShops.Element("shops-yml").Elements()
                                            select e.Value).ToList();
                        listShops = new BindingList<string>(lst);

                        ListShops = new List<XDocument>(lst.Count);

                        //Делаем список магазинов, которые будут загружаться (у которых атрибут enable = true)
                        IEnumerable<XElement> lstEnabled =
                            xdocListShops.Element("shops-yml").Descendants().Where(el => el.Attribute("enable").Value == "true");
                        if (lstEnabled.Count() == 0)
                        {
                            progress.Report("Активных магазинов нет. Проверьте вкладку Список магазинов");
                        }
                        if (bDownloadShops)
                        {
                            Task[] subtasks = new Task[lstEnabled.Count()];
                            int i = 0;
                            foreach (XElement xUrl in lstEnabled) // (string addresxml in listShops)
                            {
                                subtasks[i] = new Task(() => ListShops.Add(XDocument.Load(xUrl.Value)), TaskCreationOptions.LongRunning);
                                subtasks[i].Start();
                                i++;
                            }
                            Task.WaitAll(subtasks);
                            progress.Report($"Загружено {i} выгрузок");
                        }
                        return xdocListShops;
                    }
                    catch
                    {
                        return null;
                    }
                }, TaskCreationOptions.LongRunning);
                
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i].Start();
                }

                await Task.WhenAll(tasks); // ожидаем завершения задач
                                           
                progress.Report($"Ok!");
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }
        }

        /// <summary>
        /// Удаляет дублирующиеся товары из исключений
        /// </summary>
        /// <param name="xDocExcludes"></param>
        public static async Task RemoveDublicatsFromExcludesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    progress.Report("Удаляю дубликаты в локальном файле исключений");
                    IEnumerable<XElement> q = xdocLocExcludes.Element("Excludes").Elements().Distinct(new OfferComparer());
                    if (q.Count() > 0)
                    {
                        XElement excl = (new XElement("Excludes",
                                            new XAttribute("date", xdocLocExcludes.Element("Excludes").Attribute("date").Value)));
                        excl.Add(q);
                        excl.Save(FolderOHB_Local + "excludes.xml");
                        progress.Report("Дубликаты удалены в локальном файле, не забудте отправить исключения на сервер: Меню Исключения->Отправить исключения на сервер");
                    }
                }
                catch (Exception e)
                {
                    progress?.Report(e.Message);
                }
            });
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
        private static void AddGoods2(TreeNode treeNode, IEnumerable<XElement> xOffers)
        {
            XElement el = (XElement)treeNode.Tag;
            //if (treeNode.Nodes.Count == 0)
            if (treeNode.Nodes.Count == 0)
            {
                GetOffers(xOffers, treeNode);
            }
            else
            {
                //System.Diagnostics.Stopwatch sw = new Stopwatch();
                //sw.Start();
                //IEnumerable<TreeNode> en = GetTreeNodes(treeNode.Nodes);
                //sw.Stop();
                //Debug.WriteLine((sw.Elapsed).ToString(), "GetTreeNodes");
                //System.Diagnostics.Stopwatch sw2 = new Stopwatch();
                //sw2.Start();
                //IEnumerable<TreeNode> en2 = AllTreeNodes(treeNode.Nodes);
                //sw2.Stop();
                //Debug.WriteLine((sw2.Elapsed).ToString(), "AllTreeNodes");



                //Parallel.ForEach(AllTreeNodes(treeNode.Nodes), (tn) =>
                foreach (TreeNode tn in treeNode.Nodes)
                {
                    GetOffers(xOffers, tn);//в категории с субкатегорией, могут тоже быть товары
                    AddGoods(tn, xOffers);
                }
                //);
            }
        }


        /// <summary>
        /// Выбирает товары, привязанные к текущей категории
        /// </summary>
        /// <param name="xOffers">Список товаров</param>
        /// <param name="tnCategory">Текущая категория</param>
        /// <returns></returns>
        private static bool GetOffers(IEnumerable<XElement> xOffers, TreeNode tnCategory)
        {
            XElement gd = (XElement)tnCategory.Tag;
            IEnumerable<XElement> goods = from XElement offer in xOffers
                                          where offer.Element(categoryId).Value == gd.Attribute(id).Value
                                          select offer;

            if (goods.Count() == 0) return false;

            foreach (XElement g in goods)
            {
                TreeNode trnOffer = new TreeNode(g.Element(name).Value);
                trnOffer.Tag = g;
                tnCategory.Nodes.Add(trnOffer);
            }

            return true;
        }

        /// <summary>
        /// получает товары, не прязанные к существующим категориям, и добавляет их в корень магазина
        /// </summary>
        /// <param name="tnRoot"></param>
        /// <param name="xOffers"></param>
        private static void GetOffersUncategorized(TreeNode tnRoot, IEnumerable<XElement> xOffers)
        {
            IEnumerable<XElement> xCategories = shopTree.Element(shop).Element(categories).Elements().ToArray();
            //
            if(xCategories.Count() == 0)
            {
                List<XElement> listCat = new List<XElement>();
                foreach(XDocument xdoc in ListShops)
                {
                    listCat.AddRange(xdoc.Element(yml_catalog).Element(shop).Element(categories).Elements());
                }
                xCategories = listCat.ToArray();
            }

           // Debug.WriteLine($"xOffers.Count {xOffers.Count()}, xCategories.Count {xCategories.Count()}");

            //получаем товары, прязанные к существующим категориям
            IEnumerable<XElement> goods = (from XElement offer in xOffers
                                          from XElement category in xCategories
                                          where offer.Element(categoryId).Value == category.Attribute("id").Value
                                          select offer).ToArray();

            //получаем товары, не прязанные к существующим категориям или у которых категория, которой нет в списке
            IEnumerable<XElement> exceptGoods = xOffers.Except(goods);
            //Debug.WriteLine($"exceptGoods.Count {exceptGoods.Count()}");

            if (exceptGoods.Count() == 0) return;

            foreach (XElement g in exceptGoods)
            {
                TreeNode trnOffer = new TreeNode(g.Element("name").Value);
                trnOffer.Tag = g;
                tnRoot.Nodes.Add(trnOffer);
            }

            return;
        }

        /// <summary>
        /// Сохраняет файл исключений локально
        /// </summary>
        /// <param name="treeNodeCollection"></param>
        /// <returns></returns>
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
                    SaveXml(FolderOHB_Local + FileOHB_Excludes, excludes, out fileInfoLocalEcxludes);
                    progress.Report("Добавлено в исключения - " + excludes.Elements().Count() + " наименований");
                }
            });

        }

        /// <summary>
        /// Строит дерево и возвращает корневой узел с потомками
        /// </summary>
        /// <param name="tnRoot">Корневой узел без потомков</param>
        /// <param name="xCategories">Категории</param>
        /// <param name="xGoods">Товары</param>
        /// <returns></returns>
        public static TreeNode RebuildTree(TreeNode tnRoot, IEnumerable<XElement> xCategories, IEnumerable<XElement> xGoods)
        {
            try
            {
                tnRoot.ForeColor = Color.DarkMagenta; //root node
                //for each category to add child nodes - subcategories and offers
                //collection.AsParallel().ForAll(category => AddNodeCategory(tnRoot, category, xGoods));
                foreach (XElement category in xCategories)
                {
                    //categories node
                    TreeNode tNodeCategory = TreeNodeFromXElement(category);
                    //sequence of children elements
                    IEnumerable<XElement> offers;
                    if (xGoods.Count() > 5000)
                    {
                        offers = from XElement element in xGoods.ToArray().AsParallel()
                                                       where element.Element(categoryId)?.Value == category.Attribute(id).Value
                                                       select element;
                    }
                    else
                    {
                        offers = from XElement element in xGoods.ToArray()
                                 where element.Element(categoryId)?.Value == category.Attribute(id).Value
                                                       select element;
                    }

                    int count = offers.Count();
                    if (count == 0) continue;//If the category has not children, don't add it to the tree, take the next element.
                                             //add child to parent category node
                    if (count > 3000)
                    {
                        foreach (XElement offer in offers.AsParallel())
                            AddOfferNode(tNodeCategory, offer);
                    }
                    else
                        foreach (XElement offer in offers)
                            AddOfferNode(tNodeCategory, offer);

                    //offers.ForEach(offer => AddOfferNode(tNodeCategory, (XElement)offer));
                    tNodeCategory.Text += $" ({count})";
                    tnRoot.Nodes.Add(tNodeCategory);
                }
                //добавляем товары не привязанные к категории(или с несуществующей категорией)
                IEnumerable<XElement> unrelateChilds = xGoods.Except(GetTreeNodeOffers(tnRoot).Select(x => x.Tag as XElement)).ToArray();
                foreach (XElement offer in unrelateChilds)
                {
                    TreeNode tn = TreeNodeFromXElement(offer);
                    tn.ForeColor = Color.Indigo;
                    tnRoot.Nodes.Add(tn);
                }

                tnRoot.Text += $"({xGoods.Count()})";
                return tnRoot;
            }
            catch (Exception e)
            {
                progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
                return null;
            }

        }

        private static void AddOfferNode(TreeNode tNodeCategory, XElement offer)
        {
            TreeNode tn = TreeNodeFromXElement(offer);
            if (offer.Element(available)?.Value == "false" || offer.Attribute(available)?.Value == "false")
                tn.ForeColor = Color.MediumSlateBlue;
            else if (offer.Element(available)?.Value == "" || offer.Attribute(available)?.Value == "")
                tn.ForeColor = Color.Red;

            tNodeCategory.Nodes.Add(tn);
        }

        private static void AddNodeCategory(TreeNode tnRoot, XElement category, IEnumerable<XElement> offers)
        {
            //categories node
            TreeNode tNodeCategory = TreeNodeFromXElement(category);
            //sequence of children elements
            IEnumerable<XElement> childs = from XElement element in offers//.AsParallel()
                                           where element.Element(categoryId)?.Value == category.Attribute(id).Value
                                           //|| element.Attribute(parentId)?.Value == category.Attribute(id).Value
                                           select element;
            int count = childs.Count();
            if (count == 0) return;//If the category has not children, don't add it to the tree, take the next element.
                                     //add child to parent category node
            foreach (XElement child in childs)
            {
                tNodeCategory.Nodes.Add(TreeNodeFromXElement(child));
            }
            tNodeCategory.Text += $" ({count})";

            tnRoot.Nodes.Add(tNodeCategory);

        }
        private static TreeNode TreeNodeFromXElement(XElement xElement)
        {
            TreeNode tn;
            if (xElement.Name == "category")
            {
                tn = new TreeNode(xElement.Value);
                tn.ForeColor = Color.DarkSlateBlue;
            }
            else
            {
                tn = new TreeNode(xElement.Element(name).Value);
            }
            tn.Tag = xElement;
            tn.Name = xElement.Value;
            return tn;
        }

        public static TreeNode RebuildTree2(TreeNode tnRoot, IEnumerable<XElement> xCategories, IEnumerable<XElement> xGoods)
        {
            try
            {

                //берем корневые категории - которые не имеют parentId или parentId == 0
                foreach (XElement rootCategory in xCategories.Where(e => e.Attributes(parentId).Count() == 0
                                                                      || e.Attribute(parentId).Value == "0"))
                {
                    //tnRoot.NodeFont = new Font("Trebuchet MS", 12);
                    tnRoot.ForeColor = Color.DarkMagenta;
                    TreeNode tn = tnRoot.Nodes.Add(rootCategory.Attribute(id).Value, rootCategory.Value);
                    tn.Tag = rootCategory;
                    //заполняем корневые категории подкатегориями
                    FindSubcategories(rootCategory, tnRoot.Nodes[rootCategory.Attribute(id).Value], xCategories);
                }
                //заполняем все категории товарами
                AddGoods(tnRoot, xGoods);

                //добавляем товары не привязанные к категории (или с несуществующей категорией)
                GetOffersUncategorized(tnRoot, xGoods);

                RemoveEmptyCategory(tnRoot);

                //Расставить в дереве количество товаров в папках
                //получить все категории
                TreeNode[] treeNodes = tnRoot.Nodes
                 .OfType<TreeNode>()
                 .SelectMany(x => GetNodeAndChildren(x))
                 .Where(r => ((XElement)r.Tag)?.Name == "category")
                 .ToArray();

                foreach (TreeNode tn in treeNodes)
                {
                    int count = GetTreeNodeOffers(tn).Length;
                    tn.Text += $" ({count})";
                }

                tnRoot.Text += $" ({GetTreeNodeOffers(tnRoot).Length})";

                return tnRoot;
            }

            catch (Exception e)
            {
                progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
                return null;
            }

        }

        //public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        //{
        //    foreach (T element in source)
        //        action(element);
        //}

        /// <summary>
        /// Удаляет категории, которые не имеют товаров
        /// </summary>
        /// <param name="node"></param>
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
            //XElement excludeItem = (XElement)treeNode.Tag;
            TreeNode[] treeNodes = treeNode.Nodes
            .OfType<TreeNode>()
            .SelectMany(x => GetNodeAndChildren(x))
            .Where(r => ((XElement)r.Tag).Name == "item" || ((XElement)r.Tag).Name == "offer")
            .ToArray();

            foreach (TreeNode tn in treeNodes)
            {
                excludes.Add((XElement)treeNode.Tag);
            }
        }

        //получить все товары в TreeView
        public static TreeNode[] GetTreeNodeOffers(TreeView treeView)
        {
            TreeNode[] treeNodes = treeView.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             //.Where(r => ((XElement)r.Tag).Name != "category")
             .Where(r => ((XElement)r.Tag)?.Name == "item" || ((XElement)r.Tag)?.Name == "offer")
             .ToArray();
            return treeNodes;
        }
        public static TreeNode[] GetTreeNodeOffers(TreeNode treeNode)
        {
            TreeNode[] treeNodes = treeNode.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             .Where(r => ((XElement)r.Tag)?.Name == "item" || ((XElement)r.Tag)?.Name == "offer")
             .ToArray();
            return treeNodes;
        }
        public static TreeNode[] GetCheckedTreeNode(TreeView treeView)
        {
            TreeNode[] treeNodes = treeView.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             .Where(r => r.Checked)
             .ToArray();
            return treeNodes;
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
        public static TreeNode[] GetTreeNodes(TreeNodeCollection tnc)
        {
            TreeNode[] treeNodes = tnc
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

        public static async Task GetShopStatisticServerAsync(IProgress<string> progress)
        {
            await LoadXMLAsync();
            progress.Report($"Сервер: Категорий - {xdocRemOHBShop.Element(yml_catalog).Element(shop).Element(categories).Elements().Count()}\r\n" +
                            $"Сервер: Товаров - {xdocRemOHBShop.Element(yml_catalog).Element(shop).Element(offers).Elements().Count()}\r\n" +
                            $"Сервер: Исключений - {xdocRemExcludes.Element("Excludes").Elements().Count()}\r\n" +
                            $"Локально: Категорий - {xdocLocOHBShop.Element(yml_catalog).Element(shop).Element(categories).Elements().Count()}\r\n" +
                            $"Локально: Товаров - {xdocLocOHBShop.Element(yml_catalog).Element(shop).Element(offers).Elements().Count()}\r\n" +
                            $"Локально: Исключений - {xdocLocExcludes.Element("Excludes").Elements().Count()}\r\n"
                            );
        }

    }
    //public static T FromXElement<T>(this XElement xElement)
    //{
    //    var xmlSerializer = new XmlSerializer(typeof(T));
    //    XmlReader reader = xElement.CreateReader();
    //    reader.MoveToContent();
    //    return (T)xmlSerializer.Deserialize(reader);
    //}
    public static class IEnumerableExtention
    {
        public static void ForEach(this IEnumerable collection, Action<object> action)
        {
            foreach (object obj in collection)
            {
                action(obj);
            }
        }
    }



    // Примечание. Для запуска созданного кода может потребоваться NET Framework версии 4.5 или более поздней версии и .NET Core или Standard версии 2.0 или более поздней.
    /// <remarks/>
    [Serializable()]
    //[DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    [XmlRoot("shops-yml", Namespace = "", IsNullable = false)]
    public class ShopsYml
    {
        /// <remarks/>
        [XmlElement("yml")]
        public Yml[] YmlShop { get; set; }
    }

    /// <remarks/>
    [Serializable()]
    //[DesignerCategory("code")]
    [XmlType(AnonymousType = true)]
    public class Yml
    {
        /// <remarks/>
        [XmlAttribute()]
        [XmlText()]
        public string Enable { get; set; }

        /// <remarks/>
        [XmlText()]
        public string Value { get; set; }
    }

    namespace Helpers
    {
        public static class Files
        {
            public static string FolderOHB_Local { get; } = Application.UserAppDataPath + @"\";
            public static string FolderOHB_Remote { get; } = @"http://onebeauty.com.ua/files/";
            public static string FileOHB_Logfile { get; set; }
            public static string FileOHB_Shop { get; } = "onehomebeauty.xml";
            public static string FileOHB_ShopNew { get; } = "onehomebeauty_new.xml";
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
                    catch (Exception e)
                    {
                        OHB_Core.progress?.Report(e.Message);
                        return null;
                    }
                });

            }
            public static async Task<bool> CheckVersionsOfFilesAsync(XDocument loc, XDocument rem)
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        //XDocument loc1 = await LoadXMLAsync(FolderOHB_Local + file);
                        //XDocument rem1 = await LoadXMLAsync(FolderOHB_Remote + file);
                        //bool result = (loc?.Root.Attribute("date").Value == rem?.Root.Attribute("date").Value);
                        //bool result = loc.Root.GetHashCode().Equals(rem.Root.GetHashCode());
                        //progress.Report("Локальный " + file + " - " + loc?.Root.Attribute("date").Value + "\r\n" +
                        //                "На сервере " + file + " - " + rem?.Root.Attribute("date").Value);
                        bool result = (loc?.Root.Attribute("date").Value == rem?.Root.Attribute("date").Value);
                        //progress.Report("Локальный " + file + " - " + loc?.Root.Attribute("date").Value + "\r\n" +
                        //                "На сервере " + file + " - " + rem?.Root.Attribute("date").Value);
                        return result;
                    }
                    catch
                    {
                        return false;
                    }
                });
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
                    FtpWebRequest request = createRequest(combine(uri, currentName), WebRequestMethods.Ftp.Rename);
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
