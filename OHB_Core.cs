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
        #endregion

        private static XElement shopTree;
        private static IEnumerable<XElement> ExcludesGoods;//Collection, который содержит исключения которые не надо импортировать
        //private static XElement xExcludesGoods; //xElement, который содержит Categories и исключения

        public static IProgress<string> progress;
        public static TreeView treeViewMaster { get; set; }
        public static TreeView treeViewExcludes { get; set; }
        public static TreeNode MasterNode { get; set; }
        public static TreeNode ExcludesNode { get; set; }
        public static IList<TreeNode> TreeNodeDataShops
        {
            get
            {
                return GetTreeNodes(treeViewMaster).ToList();
            }
        }

        public static BindingList<string> listShops;//список выгрузок
        public static XDocument xdocListShops; //список выгрузок

        public static async Task InitializationAsync(IProgress<string> _progress, TreeView treeView1, TreeView treeView2)
        {
            Debug.AutoFlush = true;
            progress = _progress;
            treeViewMaster = treeView1;
            treeViewExcludes = treeView2;
            progress.Report("Cтартуем...");

            //загрузка списка магазинов
            await LoadShopsList();
            //загрузка Excludes
             ExcludesGoods = await LoadShopExcludesAsync();
            
        }

        public static async Task LoadShopsList()
        {
            xdocListShops = await LoadXMLAsync(FolderOHB_Remote + FileOHB_ListShops);
            var lst = (from e in xdocListShops.Element("shops-yml").Elements()
                       select e.Value).ToList();
            listShops = new BindingList<string>(lst);
        }

        public static async Task Update()
        {
            try
            {
                await GetShopsAsync();
                SaveXml(FolderOHB_Local + FileOHB_Shop, shopTree);
                await UploadFileAsync(FileOHB_Shop);
            }
            catch (Exception ex)
            {
                progress.Report(ex.ToString());
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
                string newFileName = Path.GetFileNameWithoutExtension(file)
                                     + DateTime.Now.ToString("dd-MM-yyyy HH:mm") + Path.GetExtension(file);

                if (client.ListDirectory().Where(f => f == fileName).Count() > 0)
                    progress.Report(await client.RenameAsync(fileName, newFileName));

                progress.Report(await client.UploadFileAsync(Path.Combine(FolderOHB_Local, fileName),
                                                                "ftp://ftp.s51.freehost.com.ua/www.onebeauty.com.ua/files/" + fileName));
                progress.Report("Ok!");
                progress.Report("==================================================");
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
        private static TreeNode GetShopsForXML(string url_shop)
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
                IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new GoodsComparer()).ToArray();
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
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }
            return null;

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
                    IEnumerable<XElement> ohbGoods = allGoods.Except(ExcludesGoods, new GoodsComparer()).ToArray();
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

                //Делаем список магазинов, которые будут загружаться (у которых атрибут enable = true)
                IEnumerable<XElement> lst = 
                    xdocListShops.Element("shops-yml").Descendants().Where(el => el.Attribute("enable").Value == "true");
                if (lst.Count() == 0)
                {
                    progress.Report("Активных магазинов нет. Проверьте вкладку Список магазинов");
                    return;
                }
                Task<TreeNode>[] tasks = new Task<TreeNode>[lst.Count()];
                int i = 0;
                foreach (XElement addresxml in lst) // (string addresxml in listShops)
                {
                    tasks[i] = new Task<TreeNode>(() => GetShopsForXML(addresxml.Value), TaskCreationOptions.LongRunning);
                    tasks[i].Start();
                    i++;
                }
                await Task.WhenAll(tasks); // ожидаем завершения задач после чего будет доступна поная секция Categories

                //foreach (XElement addresxml in xdoc.Element("shops-yml").Descendants())
                //{
                //    TreeNode tn = await GetShopsForXMLAsync(addresxml.Value);
                //}
                #region Заполняем TreeView с магазинами
                MasterNode.Tag = shopTree;
                treeViewMaster.Nodes.Add(MasterNode);
                treeViewMaster.Nodes[0].Expand();//раскрываем корневой
                #endregion
                #region Заполняем TreeView исключений
                TreeNode tnExcludes = new TreeNode("Исключения");
                progress.Report("Заполняем дерево Исключений...");
                Task task = new Task<TreeNode>(() =>
                    ExcludesNode = RebuildTree(tnExcludes,
                                shopTree.Element(shop).Element(categories).Elements(),
                                ExcludesGoods),
                                TaskCreationOptions.LongRunning);
                task.Start();
                await Task.WhenAll(task);
                ExcludesNode.Tag = new XElement("Excludes");
                progress.Report("исключения - готово!");
                treeViewExcludes.Nodes.Add(ExcludesNode);
                treeViewExcludes.Nodes[0].Expand();//раскрываем корневой
                #endregion

                shopTree.Save(FolderOHB_Local + FileOHB_Shop);//сохраняем локальный файл

                СommonReport(time_update);
            }
            catch (Exception e)
            {
                progress.Report("GetShopsAsync - " + e.Message);
            }

        }

        public static void СommonReport(string time_update)
        {
            progress.Report("Всего категорий - " + shopTree.Element(shop).Element(categories).Elements().Count()
                           + "; товаров - " + shopTree.Element(shop).Element(offers).Elements().Count());
            FileInfo f = new FileInfo(FolderOHB_Local + FileOHB_Shop);
            progress.Report("Локальный файл - " + f.FullName + "\r\n" + Math.Round((double)f.Length / 1000000, 2) + " Mb");
            progress.Report("Время локального обновления - " + time_update);
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
        private static async Task <IEnumerable<XElement>> LoadShopExcludesAsync()
        {
            try
            {
                //***************************************************************
                //загружаем магазин
                XDocument xDocExcludes;
                using (var httpclient = new HttpClient())
                {
                    var response1 = await httpclient.GetAsync(Path.Combine(FolderOHB_Remote, FileOHB_Excludes));
                    var response2 = await httpclient.GetAsync(Path.Combine(FolderOHB_Remote, FileOHB_Shop));
                    xDocExcludes = XDocument.Load(await response1.Content.ReadAsStreamAsync());
                 }

                return xDocExcludes.Element("Excludes").Elements();
            }
            catch (XmlException xmlEx)
            {
                progress.Report(xmlEx.Message);
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
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

        //Выбирает тег <offer> из xml
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
        private static void GetOffersUncategorized(TreeNode tnRoot, IEnumerable<XElement> xOffers)
        {
            IEnumerable<XElement> xCategories = shopTree.Element(shop).Element(categories).Elements();

            Debug.WriteLine($"xOffers.Count {xOffers.Count()}, xCategories.Count {xCategories.Count()}");

            //получаем товары, прязанные к существующим категориям
            IEnumerable<XElement> goods = from XElement offer in xOffers
                                          from XElement category in xCategories
                                          where offer.Element(categoryId).Value == category.Attribute("id").Value
                                          select offer;

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

        public static TreeNode RebuildTree(TreeNode tnRoot, IEnumerable<XElement> xCategories, IEnumerable<XElement> xGoods)
        {
            try
            {
                //IEnumerable<XElement> _categories = shopTree.Element(shop).Element(categories).Elements();
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
                progress.Report("Error RebuildTree(" + tnRoot.Name + ") - " + e.Message);
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

        public static async Task GetShopStatisticServer(IProgress<string> progress)
        {
            progress.Report($"Статистика...");
            //XDocument loc = await LoadXMLAsync(FolderOHB_Local + file);
            XDocument remoteShop = await LoadXMLAsync(FolderOHB_Remote + FileOHB_Shop);
            if (remoteShop==null)
            {
                progress.Report($"{FolderOHB_Remote + FileOHB_Shop} не найден");
                return;
            }
            //список категорий
            IEnumerable<XElement> xCategories = remoteShop.Element(yml_catalog).Element(shop).Element(categories).Elements();
            //список всех товаров
            IEnumerable<XElement> allGoods = remoteShop.Element(yml_catalog).Element(shop).Element(offers).Elements();
            //progress.Report($"Категорий - {xCategories.Count()} \r\nТоваров всего - {allGoods.Count()}");

            //XDocument remoteExcludes = await LoadXMLAsync(FolderOHB_Remote + FileOHB_Excludes);
            //progress.Report($"Исключений - {remoteExcludes.Element("Excludes").Elements().Count()}");
            int x1 = xCategories.Count();
            int x2 = allGoods.Count();
            int x3 = ExcludesGoods.Count(); //remoteExcludes.Element("Excludes").Elements().Count();
            progress.Report($"Категорий - {x1} \r\nТоваров всего - {x2}\r\n" +
                  $"Исключений - {x3}\r\n" +
                  $"Общее количество - {x1 + x2 + x3}");
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
            public static async Task<bool> CheckVersionsOfFilesAsync(string file, IProgress<string> progress)
            {
                try
                {
                    progress.Report($"Проверяю версию {file}...");
                    XDocument loc = await LoadXMLAsync(FolderOHB_Local + file);
                    XDocument rem = await LoadXMLAsync(FolderOHB_Remote + file);
                    bool result = (loc?.Root.Attribute("date").Value == rem?.Root.Attribute("date").Value);
                    //bool result = loc.Root.GetHashCode().Equals(rem.Root.GetHashCode());
                    progress.Report("Локальный " + file + " - " + loc?.Root.Attribute("date").Value + "\r\n" +
                                    "На сервере " + file + " - " + rem?.Root.Attribute("date").Value);
                    return result;
                }
                catch(Exception e)
                {
                    progress.Report(e.Message);
                    return false;
                }
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
