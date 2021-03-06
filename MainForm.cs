﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;


namespace FileExplorer
{
    public partial class MainForm : Form
    {
        //Для перемещения файлов.
        private bool mIsMove;

    
        //Исходный путь к файлам для копирования и вставки.
        private readonly List<string> mListSourcesPath;

        //----------------------------------------------------------

        //Имя файла для поиска.
        private string mSearchFileName;

        //Показывать скрытые файлы.
        private bool mShowHidden;

        //Текущий путь.
        private string mCurrentPath;

        //Выбранный узел дерева с директориями.
        private TreeNode mDirViewCurrentNode;

        //Дерево со всеми файловыми записями.
        private TreeItem mRootFileTree;
        private TreeItem mTreeCurrentNode;

   

        public string CurrentPath
        {
            get => mCurrentPath;
            set
            {
                mCurrentPath = value;
                addressInput.Text = mCurrentPath;
            }
        }


        public MainForm()
        {
            ThreadPool.SetMaxThreads(25, 25);
            ClosePreviousInstance();
            mListSourcesPath = new List<string>(200);
            mIsMove = false;
            mShowHidden = false;
            mDirViewCurrentNode = null;
            mCurrentPath = string.Empty;
            mSearchFileName = string.Empty;

            InitializeComponent();
            InitDisplay();
        }


        public void ClosePreviousInstance()
        {
            Process[] pname = Process.GetProcessesByName(AppDomain
                .CurrentDomain
                .FriendlyName
                .Remove(AppDomain.CurrentDomain.FriendlyName.Length - 4));

            if (pname.Length > 1)
                pname.First(p => p.Id != Process.GetCurrentProcess().Id).Kill();
        }


        protected override void OnResize(EventArgs e)
        {
            addressInput.Width = Width - 290;
        }


        private void NewFolder_Click(object sender, EventArgs e)
        {
            Console.WriteLine("NewFolder_Click");
            try
            {
                string path = Path.Combine(CurrentPath, "New folder");
                string newFolderPath = path;

                int num = 1;
                while (Directory.Exists(newFolderPath))
                {
                    newFolderPath = path + " (" + num + ")";
                    num++;
                }

                DirectoryInfo dirInfo = Directory.CreateDirectory(newFolderPath);
                AddItemOnListView(dirInfo.FullName, false);
                UpdateTreeView();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }
        }


        private void NewFile_Click(object sender, EventArgs e)
        {
            Console.WriteLine("NewFile_Click");
            NewFileForm newFileForm = new NewFileForm(CurrentPath);
            newFileForm.ShowDialog();
            UpdateListView();
        }


        private void Copy_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Copy_Click");
            SetCopyFilesSourcePaths();
            mIsMove = false;
        }


        private void Cut_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Cut_Click");
            SetCopyFilesSourcePaths();
            mIsMove = true;
        }


        private void Paste_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Paste_Click");

            //Нет файлов для вставки.
            if (!mListSourcesPath.Any())
                return;

            foreach (string path in mListSourcesPath)
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                    MoveToOrCopyToDirectoryBySourcePath(path);
                else
                    MoveToOrCopyToFileBySourcePath(path);
            }

            //Обновить виджеты.
//            UpdateListView(CurrentPath);
//            UpdateTreeView(mDirViewCurrentNode);

            UpdateTreeView();
            mTreeCurrentNode.Childs.Clear();
            FileSystem.LoadFileTreeAsync(mTreeCurrentNode);
            UpdateListView();
        }


        private void Rename_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Rename_Click");
            if (listViewFiles.SelectedItems.Count > 0)
                listViewFiles.SelectedItems[0].BeginEdit();
        }


        private void Delete_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Delete_Click");

            if (listViewFiles.SelectedItems.Count > 0)
            {
                DialogResult dialogResult = MessageBox.Show("Are you sure to delete?",
                    "Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation
                );

                if (dialogResult == DialogResult.No)
                    return;
                else
                {
                    try
                    {
                        Queue<string> files = new Queue<string>(20);

                        foreach (ListViewItem item in listViewFiles.SelectedItems)
                        {
                            string path = item.Tag.ToString();

                            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                                Directory.Delete(path, true);
                            else
                                File.Delete(path);

                            listViewFiles.Items.Remove(item);
                            files.Enqueue(path);
                        }

                        for (int i = mTreeCurrentNode.Childs.Count - 1; i >= 0; i--)
                        {
                            TreeItem node = mTreeCurrentNode.Childs[i];
                            if (files.Contains(node.ItemData))
                                mTreeCurrentNode.Childs.Remove(node);
                        }

                        UpdateTreeView();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
        }


        private void Refresh_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Refresh_Click");
            UpdateListView();
            UpdateTreeView();
        }


        private void HiddenFilesTsmi_CheckStateChanged(object sender, EventArgs e)
        {
            mShowHidden = hiddenFilesTsmi.Checked;
            UpdateListView();
            UpdateTreeView();
        }


        private void FileTreeCountTsmi_Click(object sender, EventArgs e)
        {
            fileEntryCountLabel.Text = TreeItem.Count.ToString();
        }


        private void Properties_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Properties_Click");

            PropertiesForm form;
            //Нет выбранных файлов\папок.
            if (listViewFiles.SelectedItems.Count == 0)
            {
                //Показать свойства текущей папки.
                form = new PropertiesForm(CurrentPath);
            }
            else
            {
                //Показать свойства первого выбранного файла
                form = new PropertiesForm(listViewFiles.SelectedItems[0].Tag.ToString());
            }

            form.Show();
        }


        private void MainContextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            Console.WriteLine("MainContextMenuStrip_Opening");

            //Получить координаты курсора.
            Point curPoint = listViewFiles.PointToClient(Cursor.Position);

            //Получить ListViewItem по координатам курсора.
            ListViewItem item = listViewFiles.GetItemAt(curPoint.X, curPoint.Y);

            //Если курсор на итеме.
            if (item != null)
            {
                copyTsmi.Visible = true;
                pasteContexMenu.Visible = true;
                cutTsmi.Visible = true;
                deleteTsmi.Visible = true;
                renameTsmi.Visible = true;
                newFolderContextMenu.Visible = true;
            }
            else
            {
                copyTsmi.Visible = false;
                pasteContexMenu.Visible = true;
                cutTsmi.Visible = false;
                deleteTsmi.Visible = false;
                renameTsmi.Visible = false;
                newFolderContextMenu.Visible = true;
            }
        }


        //Перейти вверх на 1 уровень.
        private void UpArrowBtn_Click(object sender, EventArgs e)
        {
            Console.WriteLine("UpArrowBtn_Click");

            TreeItem upNode = mTreeCurrentNode.ParentItem;
            if (upNode != mRootFileTree && upNode != null)
            {
                mTreeCurrentNode = upNode;
                CurrentPath = mTreeCurrentNode.ItemData.WithoutLongPathPrefix();
                UpdateListView();
            }
        }


        private void AddressInput_KeyDown(object sender, KeyEventArgs e)
        {
            Console.WriteLine("AddressInput_KeyDown");

            if (e.KeyCode == Keys.Enter)
            {
                if (string.IsNullOrWhiteSpace(addressInput.Text)) return;
                string newPath = addressInput.Text.AddLongPathPrefix();
                if (!Directory.Exists(newPath)) return;

                mTreeCurrentNode = FileSystem.GetFileTreeNodeByPath(newPath, mRootFileTree);
                UpdateListView();
            }
        }


        private void ListViewFiles_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            Console.WriteLine("ListViewFiles_AfterLabelEdit");
            if (string.IsNullOrEmpty(e.Label))
            {
                e.CancelEdit = true;
                return;
            }

            string newName = e.Label;
            ListViewItem selectedItem = listViewFiles.SelectedItems[0];

            //Если имя не изменилось.
            if (newName == selectedItem.Text)
            {
                return;
            }
            else if (!FileSystem.IsValidFileName(newName))
            {
                MessageBox.Show("The file name can't contain any of the following characters:\n" + "\t\\/:*?\"<>|",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                e.CancelEdit = true;
            }
            else
            {
                Computer computer = new Computer();

                if (File.GetAttributes(selectedItem.Tag.ToString()).HasFlag(FileAttributes.Directory))
                {
                    //Если уже есть папка с таким именем.
                    if (Directory.Exists(Path.Combine(CurrentPath, newName)))
                    {
                        MessageBox.Show("Target folder already contains a folder with that name！",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );

                        e.CancelEdit = true;
                    }
                    else
                    {
                        computer.FileSystem.RenameDirectory(selectedItem.Tag.ToString(), newName);

                        //Обновить тэг выбранного элемента.
                        DirectoryInfo directoryInfo = new DirectoryInfo(selectedItem.Tag.ToString());
                        string parentPath = directoryInfo.Parent.FullName;
                        string newPath = Path.Combine(parentPath, newName);
                        selectedItem.Tag = newPath;
                    }
                }
                else
                {
                    //Если уже есть файл с таким именем.
                    if (File.Exists(Path.Combine(CurrentPath, newName)))
                    {
                        MessageBox.Show("Target folder already contains a file with that name！",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );

                        e.CancelEdit = true;
                    }
                    else
                    {
                        computer.FileSystem.RenameFile(selectedItem.Tag.ToString(), newName);

                        //Обновить тэг выбранного элемента.
                        FileInfo fileInfo = new FileInfo(selectedItem.Tag.ToString());
                        string parentPath = Path.GetDirectoryName(fileInfo.FullName);
                        string newPath = Path.Combine(parentPath, newName);
                        selectedItem.Tag = newPath;
                    }
                }
            }

            UpdateTreeView();
            mTreeCurrentNode.Childs.Clear();
            FileSystem.LoadFileTreeAsync(mTreeCurrentNode);
            UpdateListView();
        }


        //Двойное нажатие по ListViewItem.
        private void ListViewFiles_ItemActivate(object sender, EventArgs e)
        {
            Console.WriteLine("ListViewFiles_ItemActivate");
            Open();
        }


        //При выборе узла на TreeView, обновляет список файлов ListView для этого узла.
        private void DirectoryTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            Console.WriteLine("DirectoryTreeView_AfterSelect");
            mDirViewCurrentNode = e.Node;
            mTreeCurrentNode = FileSystem.GetFileTreeNodeByPath(e.Node.Tag.ToString(), mRootFileTree);
            CurrentPath = mTreeCurrentNode.ItemData.WithoutLongPathPrefix();
            UpdateListView();
        }


        private void DirectoryTreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            Console.WriteLine("DirectoryTreeView_BeforeExpand");
            //Загрузить дочерние узлы выбранного узла до того, как он будет развернут.
            mDirViewCurrentNode = e.Node;
            UpdateTreeView();
        }


        private void SearchInput_Enter(object sender, EventArgs e)
        {
            Console.WriteLine("SearchInput_Enter");
            searchInput.Text = string.Empty;
        }


        private void SearchInput_Leave(object sender, EventArgs e)
        {
            Console.WriteLine("SearchInput_Leave");
            searchInput.Text = "Search:";
            statusBarFileNum.Text = $"{listViewFiles.Items.Count} items";
        }


        private void SearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            Console.WriteLine("SearchInput_KeyDown");

            if (e.KeyCode == Keys.Enter)
            {
                if (string.IsNullOrWhiteSpace(searchInput.Text))
                    return;

                listViewFiles.Items.Clear();
                statusBarFileNum.Text = "0 items";
                mSearchFileName = searchInput.Text.ToLower();
                ThreadPool.QueueUserWorkItem(new WaitCallback(SearchInTree), mTreeCurrentNode);
            }
        }


        private void InitDisplay()
        {
            Console.WriteLine("InitDisplay");
            try
            {
                mRootFileTree = new TreeItem("ROOT", null);
                listIcons.Images.Add("drive", ShellIcon.DriveIcon); //для всех дисков и папок 
                listIcons.Images.Add("folder", ShellIcon.FolderIcon); //одинаковая иконка

                foreach (DriveInfo info in DriveInfo.GetDrives())
                {
                    string label = string.IsNullOrWhiteSpace(info.VolumeLabel) ? "Disk" : info.VolumeLabel;
                    label += $"({info.Name.Split('\\')[0]})";

                    TreeNode driveNode = directoryTreeView.Nodes.Add(label);
                    driveNode.Tag = info.Name;
                    driveNode.ImageKey = "drive";
                    driveNode.Nodes.Add(string.Empty); //Добавляю пустой узел, чтобы появился значок '+'.

                    TreeItem childNode = new TreeItem(info.Name.AddLongPathPrefix(), mRootFileTree);
                    mRootFileTree.AddChild(childNode);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(FileSystem.LoadFileTreeAsync), childNode);
                }

                mTreeCurrentNode = mRootFileTree.Childs.First();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }
        }


        //Обновить TreeView для текущего узла (TreeNode mDirViewCurrentNode);
        private void UpdateTreeView()
        {
            Console.WriteLine("\t\t\t\t\t\tLoadChildNodes");
            try
            {
                mDirViewCurrentNode.Nodes.Clear(); //удалить мнимый узел
                DirectoryInfo directoryInfo = new DirectoryInfo(mDirViewCurrentNode.Tag.ToString());

                //Cписок папок.
                foreach (DirectoryInfo dir in directoryInfo.GetDirectories())
                {
                    try
                    {
                        dir.GetAccessControl();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    var attr = File.GetAttributes(dir.FullName);
                    if (attr.HasFlag(FileAttributes.System)
                        || (attr.HasFlag(FileAttributes.Hidden) && !mShowHidden))
                    {
                        continue;
                    }

                    TreeNode childNode = mDirViewCurrentNode.Nodes.Add(dir.Name);
                    childNode.Tag = dir.FullName;
                    childNode.ImageKey = "folder";
                    childNode.Nodes.Add(string.Empty); //Добавляю пустой узел, чтобы появился значок '+'.
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }
        }


        //Обновить ListView для текущего узла (TreeItem mTreeCurrentNode);
        private void UpdateListView()
        {
            Console.WriteLine("\t\t\t\t\t\tShowFilesList");
            //BeginUpdate, EndUpdate - нужны при добавление большого количества элементов,
            //чтобы не перерисовывало после каждого.
            listViewFiles.BeginUpdate();
            listViewFiles.Items.Clear();

            try
            {
                Queue<string> files = new Queue<string>(20);

                foreach (TreeItem childNode in mTreeCurrentNode.Childs)
                {
                    string fullName = childNode.ItemData;
                    FileAttributes attr = File.GetAttributes(fullName);

                    if (attr.HasFlag(FileAttributes.System)
                        || (attr.HasFlag(FileAttributes.Hidden) && !mShowHidden))
                    {
                        continue;
                    }

                    if (attr.HasFlag(FileAttributes.Directory))
                        AddItemOnListView(fullName, false);
                    else
                        files.Enqueue(fullName);
                }

                foreach (string fullName in files)
                {
                    AddItemOnListView(fullName, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }

            //Количество файлов\папок в статус бар.
            statusBarFileNum.Text = listViewFiles.Items.Count + " items";
            listViewFiles.EndUpdate();
        }


        private void AddItemOnListView(string fullPath, bool isFile)
        {
            if (isFile)
            {
                FileInfo fileInfo = new FileInfo(fullPath);
                ListViewItem item = listViewFiles.Items.Add(fileInfo.Name);

                //Исполняемые файлы и ярлыки могут иметь разные иконки.
                if (fileInfo.Extension == ".exe" || fileInfo.Extension == ".lnk" ||
                    fileInfo.Extension == string.Empty)
                {
                    if (!listIcons.Images.ContainsKey(fileInfo.FullName))
                    {
                        Icon fileIcon = ShellIcon.GetLargeIcon(fileInfo.FullName.WithoutLongPathPrefix());
                        listIcons.Images.Add(fileInfo.FullName, fileIcon);
                    }

                    item.ImageKey = fileInfo.FullName;
                }
                else
                {
                    //Для остальных типов, иконка одинаковая для всех файлов этого расширения.
                    if (!listIcons.Images.ContainsKey(fileInfo.Extension))
                    {
                        Icon fileIcon = ShellIcon.GetLargeIcon(fileInfo.FullName.WithoutLongPathPrefix());
                        listIcons.Images.Add(fileInfo.Extension, fileIcon);
                    }

                    item.ImageKey = fileInfo.Extension;
                }

                item.Tag = fileInfo.FullName;
                item.SubItems.Add(fileInfo.LastWriteTime.ToString("dd.mm.yyyy HH:mm"));
                item.SubItems.Add(string.IsNullOrEmpty(fileInfo.Extension) ? "File" : fileInfo.Extension);
                item.SubItems.Add(FileSystem.FileSizeStr(fileInfo.Length));
            }
            else
            {
                DirectoryInfo dirInfo = new DirectoryInfo(fullPath);
                ListViewItem item = listViewFiles.Items.Add(dirInfo.Name, "folder");
                item.Tag = dirInfo.FullName;
                item.SubItems.Add(dirInfo.LastWriteTime.ToString("dd.mm.yyyy HH:mm"));
                item.SubItems.Add("Folder");
                item.SubItems.Add(string.Empty); //Для папок считать размер слишком долго.
            }
            statusBarFileNum.Text = $"{listViewFiles.Items.Count} items";
        }


        private void AddItemOnListViewAsync(string fullPath, bool isFile)
        {
            if (listViewFiles.InvokeRequired)
            {
                listViewFiles.Invoke(new MethodInvoker(delegate { AddItemOnListView(fullPath, isFile); }));
            }
            else
            {
                AddItemOnListView(fullPath, isFile);
            }
        }


        private void Open()
        {
            Console.WriteLine("\t\t\t\t\t\tOpen");

            if (listViewFiles.SelectedItems.Count > 0)
            {
                try
                {
                    string path = listViewFiles.SelectedItems[0].Tag.ToString();
                    if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                    {
                        //Открыть папку.
                        CurrentPath = path.WithoutLongPathPrefix();
                        foreach (var childNode in mTreeCurrentNode.Childs)
                        {
                            if (childNode.ItemData == path)
                            {
                                mTreeCurrentNode = childNode;
                                break;
                            }
                        }
                        UpdateListView();
                    }
                    else
                    {
                        //Открыть файл.
                        Process.Start(path);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }


        //Получить пути к файлам для копирования.
        private void SetCopyFilesSourcePaths()
        {
            Console.WriteLine("\t\t\t\t\t\tSetCopyFilesSourcePaths");

            if (listViewFiles.SelectedItems.Count > 0)
            {
                mListSourcesPath.Clear();
                foreach (ListViewItem item in listViewFiles.SelectedItems)
                    mListSourcesPath.Add(item.Tag.ToString());
            }
        }


        private void MoveToOrCopyToFileBySourcePath(string sourcePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(sourcePath);
                string destPath = Path.Combine(CurrentPath, fileInfo.Name);

                if (destPath == sourcePath || File.Exists(destPath))
                {
                    string path = Path.Combine(fileInfo.DirectoryName,
                        Path.GetFileNameWithoutExtension(fileInfo.FullName));
                    int num = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = path + $" - Copy ({num++}){fileInfo.Extension}";
                    }
                }

                if (mIsMove)
                    fileInfo.MoveTo(destPath);
                else
                    fileInfo.CopyTo(destPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }
        }


        private void MoveToOrCopyToDirectoryBySourcePath(string sourcePath)
        {
            try
            {
                DirectoryInfo sourceDirectoryInfo = new DirectoryInfo(sourcePath);
                string destPath = Path.Combine(CurrentPath, sourceDirectoryInfo.Name);

                if (destPath == sourcePath || Directory.Exists(destPath))
                {
                    string path = destPath;
                    int num = 1;
                    while (Directory.Exists(destPath))
                    {
                        destPath = path + $" - Copy ({num++})";
                    }
                }

                if (mIsMove)
                {
                    FileSystem.CopyAndPasteDirectory(sourceDirectoryInfo, new DirectoryInfo(destPath));
                    Directory.Delete(sourcePath, true);
                }
                else
                {
                    FileSystem.CopyAndPasteDirectory(sourceDirectoryInfo, new DirectoryInfo(destPath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine(ex.StackTrace);
            }
        }


        private void SearchInTree(object _node)
        {
            TreeItem node = (TreeItem) _node;

            foreach (TreeItem childNode in node.Childs)
            {
                string fullName = childNode.ItemData;
                FileAttributes attr = File.GetAttributes(fullName);
                string name = fullName.Split('\\').Last().ToLower();


                if (attr.HasFlag(FileAttributes.Directory))
                {
                    if (name.Contains(mSearchFileName))
                    {
                        AddItemOnListViewAsync(fullName, false);
                    }
                    ThreadPool.QueueUserWorkItem(new WaitCallback(SearchInTree), childNode);
                }
                else
                {
                    if (name.Contains(mSearchFileName))
                    {
                        AddItemOnListViewAsync(fullName, true);
                    }
                }
            }
        }
    }
}