﻿using Newtonsoft.Json;
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

        private static XElement shopTree;
        private static IEnumerable<XElement> ExcludesGoods { get; set; }//Collection, который содержит исключения которые не надо импортировать
        //private static XElement xExcludesGoods; //xElement, который содержит Categories и исключения
        private static IEnumerable<XElement> NewGoods { get; set; }

        public static IProgress<string> progress;
        public static TreeView treeViewMaster { get; set; }
        public static TreeView treeViewExcludes { get; set; }
        public static TreeView treeViewNewOffers { get; set; }
        public static TreeNode MasterNode { get; set; }
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

        public static async Task InitializationAsync(IProgress<string> _progress, TreeView treeView1, TreeView treeView2, TreeView treeViewNew)
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
            await LoadXMLAsync();
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
            treeViewExcludes.Nodes.Add(ExcludesNode);
            treeViewExcludes.Nodes[0].Expand();//раскрываем корневой
        }

        private static void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            AddExcludesInTreeView();
        }

        public static void AddExcludesInTreeView(bool buildTree = false)
        {
            try
            {
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
                progress.Report("Дерево исключений - построено!");
                #endregion
            }
            catch(Exception e)
            {
                progress.Report($"AddExcludesInTreeView() - {e.Message}");
            }
        }


        //public static async Task Update()
        //{
        //    try
        //    {
        //        await GetShopsAsync();
        //        SaveXml(FolderOHB_Local + FileOHB_Shop, shopTree);
        //        await UploadFileAsync(FileOHB_Shop);
        //    }
        //    catch (Exception ex)
        //    {
        //        progress.Report(ex.ToString());
        //    }
        //}


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
            string qry_products = "products/list?limit = 10000 & group_id = ";
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
        private static void GetShopsForXML(XDocument xYMLCatalog)
        {
            try
            {
                //***************************************************************
                //загружаем магазин
                Uri uri = new Uri(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url)?.Value);
                progress.Report(uri.OriginalString + " building tree...");
                //XDocument xYMLCatalog = XDocument.Load(url_shop);

                string nameShop = uri.Host + uri.PathAndQuery.Replace("/", "-");
                xYMLCatalog.Save(nameShop + ".xml");
                //список категорий
                IEnumerable<XElement> xCategories = xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements();
                //список всех товаров
                IEnumerable<XElement> allGoods = xYMLCatalog.Element(yml_catalog).Element(shop).Element(offers).Elements();
                //список товаров с учетом исключений | Без .ToArray() - очень долго считает

                System.Diagnostics.Stopwatch sw = new Stopwatch();
                sw.Start();
                //IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new GoodsComparer()).ToArray();
                IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new OfferComparer()).ToArray();
                sw.Stop();
                Debug.WriteLine($"{nameShop} except - {sw.Elapsed}");
                //Debug.WriteLine((sw.Elapsed).ToString(), "time except");

                //добавляем Категории в общее дерево
                shopTree.Element(shop).Element(categories).Add(xYMLCatalog.Element(yml_catalog).Element(shop).Element(categories).Elements());
                //добавляем Товары в общее дерево 
                shopTree.Element(shop).Element(offers).Add(ohbGoods);

                XAttribute xCatalogAttribute = xYMLCatalog.Element(yml_catalog).Attribute(date);
                DateTime lastUpdate = DateTime.Parse(xCatalogAttribute.Value);//дата последнего обновления

                //************************  строим дерево категорий-подкатегорий  ******************************
                //Добавляем магазин в TreeView
                //TreeNode rootCatalog = new TreeNode(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url).Value +
                //                        " - " + lastUpdate.ToString() + " (" + xCategories.Count() + ")");
                TreeNode rootCatalog = new TreeNode($"{nameShop} - {lastUpdate.ToString()} ({xCategories.Count()})");
                //Строим дерево
                RebuildTree(rootCatalog, xCategories, ohbGoods);
                rootCatalog.Tag = xYMLCatalog.Element(yml_catalog);

                MasterNode.Nodes.Add(rootCatalog);
                progress.Report($"{nameShop} Ok!: категорий - " + xCategories.Count() + 
                                ";\r\n  товаров в выгрузке - " + allGoods.Count() +
                                ";\r\n  товаров с учетом исключений - " + ohbGoods.Count() +
                                ";\r\nобновлено: " + xYMLCatalog.Element(yml_catalog).Attribute(date).Value +
                                "\r\n————————————————————————————————————————————————————————");

               
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
                    DateTime lastUpdate = DateTime.Parse(xCatalogAttribute.Value);//дата последнего обновления

                    //************************  строим дерево категорий-подкатегорий  ******************************
                    //Добавляем магазин в TreeView
                    //TreeNode rootCatalog = new TreeNode(xYMLCatalog.Element(yml_catalog).Element(shop).Element(url).Value +
                    //                        " - " + lastUpdate.ToString() + " (" + xCategories.Count() + ")");
                    TreeNode rootCatalog = new TreeNode($"{nameShop} - {lastUpdate.ToString()} ({xCategories.Count()})");
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

                shopTree =
                    new XElement(yml_catalog, new XAttribute(date, time_update),
                        new XElement(shop,
                            new XElement(categories),
                            new XElement(offers)));

                MasterNode = new TreeNode("One Home Beauty - " + time_update);

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
                treeViewMaster.Nodes.Add(MasterNode);
                treeViewMaster.Nodes[0].Expand();//раскрываем корневой
                #endregion

                shopTree.Save(FolderOHB_Local + FileOHB_Shop);//сохраняем локальный файл

                //TODO load except between local and remote shops 
                #region Заполняем TreeViewNew новыми поступлениями
                progress.Report("Новые поступления...");
                NewGoods = shopTree.Element(shop).Element(offers).Elements()
                    .Except(xdocRemOHBShop.Element(yml_catalog).Element(shop).Element(offers).Elements(), new OfferComparer()).ToArray();
                NewOffersNode = new TreeNode($"Новые поступления на {time_update} - {NewGoods.Count()}");
                //Строим дерево
                RebuildTree(NewOffersNode, shopTree.Element(shop).Element(categories).Elements(), NewGoods);
                progress.Report("Новые поступления дерево построено!");
                NewOffersNode.Tag = shopTree;
                treeViewNewOffers.Nodes.Add(NewOffersNode);
                treeViewNewOffers.Nodes[0].Expand();//раскрываем корневой
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

            // If Equals() returns true for a pair of objects 
            // then GetHashCode() must return the same value for these objects.

            public int GetHashCode(XElement product)
            {
                //Check whether the object is null
                if (object.ReferenceEquals(product, null)) return 0;

                ////Get hash code for the Name field if it is not null.
                //int hashProductName = product.Value == null ? 0 : product.Value.GetHashCode();

                ////Get hash code for the Code field.
                //int hashProductCode = product.Name.GetHashCode();
                //Get hash code for the Name field if it is not null.
                int hashProductId = product.Attribute(id).Value == null ? 0 : product.Attribute(id).Value.GetHashCode();
                //int hashProductName = product.Element(name).Value == null ? 0 : product.Element(name).Value.GetHashCode();
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


                //Get hash code for the Code field.
                //int hashProductCode = product.Element(categoryId).Value == null ? 0 : product.Element(categoryId).Value.GetHashCode(); 


                //Calculate the hash code for the product.
                //return hashProductId * hashProductName * hashProductVendor;
                return hashProductId * hashProductVendor;
                //return hashProductName;
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

            Debug.WriteLine($"xOffers.Count {xOffers.Count()}, xCategories.Count {xCategories.Count()}");

            //получаем товары, прязанные к существующим категориям
            IEnumerable<XElement> goods = (from XElement offer in xOffers
                                          from XElement category in xCategories
                                          where offer.Element(categoryId).Value == category.Attribute("id").Value
                                          select offer).ToArray();

            //получаем товары, не прязанные к существующим категориям или у которых категория, которой нет в списке
            IEnumerable<XElement> exceptGoods = xOffers.Except(goods);
            Debug.WriteLine($"exceptGoods.Count {exceptGoods.Count()}");

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

                //TODO: добавляем товары не привязанные к категории (или с несуществующей категорией)
                GetOffersUncategorized(tnRoot, xGoods);

                RemoveEmptyCategory(tnRoot);
                return tnRoot;
            }

            catch (Exception e)
            {
                progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
                return null;
            }

        }
        
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


            //if (excludeItem.Name == "item" || excludeItem.Name == "offer")
            //{
            //    excludes.Add(excludeItem);
            //}
            //else
            //{
            //    foreach (TreeNode tn in treeNode.Nodes)
            //    {
            //        GetExcludesGoods(excludes, tn);
            //    }
            //}

        }

        //получить все товары в TreeView
        public static TreeNode[] GetTreeNodeOffers(TreeView treeView)
        {
            TreeNode[] treeNodes = treeView.Nodes
             .OfType<TreeNode>()
             .SelectMany(x => GetNodeAndChildren(x))
             //.Where(r => ((XElement)r.Tag).Name != "category")
             .Where(r => ((XElement)r.Tag).Name == "item" || ((XElement)r.Tag).Name == "offer")
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
        //public static void GetCountOfItemsInTreeView(TreeNodeCollection tnc, ref int i)
        //{
        //    foreach (TreeNode node in tnc)
        //    {
        //        XElement el = (XElement)node.Tag;
        //        if (el.Name != "category")
        //        {
        //            i++;
        //        }
        //        else
        //        {
        //            GetCountOfItemsInTreeView(node.Nodes, ref i);
        //        }

        //    }
        //}


        //public static int GetCountOfItemsInTreeView(TreeNode node)
        //{
        //    IEnumerable<TreeNode> res = new[] { node }.Concat(node.Nodes
        //                                    .OfType<TreeNode>()
        //                                    .Where(n => ((XElement)n.Tag).Name != "category")
        //                                    .SelectMany(x => GetNodeAndChildren(x)));

        //    return res.Count();
        //}

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
