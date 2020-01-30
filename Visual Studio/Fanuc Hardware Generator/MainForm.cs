using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Fanuc_Hardware_Generator
{
    public partial class MainForm : Form
    {
        private string savePath;
        private List<TreeNode> treeNodes;
        private string libraryFolder;
        private string libraryComponents;

        public MainForm()
        {
            InitializeComponent();
            string workingDirectory = Environment.CurrentDirectory;
            libraryFolder = Path.Combine(workingDirectory, "Library");
            libraryComponents = Path.Combine(workingDirectory, "Library", "components");
            treeNodes = new List<TreeNode>();

            if (!Directory.Exists(libraryFolder) || !Directory.Exists(libraryComponents))
            {
                MessageBox.Show("Library not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DisableControls(this);
            }
            else
            {
                PopulateTree();
            }
        }

        #region Events
        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            TreeNode tn = new TreeNode("Hardware");

            foreach (var node in treeNodes.Where(s => s.Name.ToLower().Contains(txtSearch.Text.ToLower())))
            {
                tn.Nodes.Add((TreeNode)node.Clone());
            }

            if (tn.Nodes.Count == 0)
                tn.Text = "Nothing found";

            treeViewHardware.Nodes.Clear();
            treeViewHardware.Nodes.Add(tn);
            treeViewHardware.ExpandAll();
        }

        private void treeViewHardware_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Right && treeViewHardware.SelectedNode != null)
            {
                AddHardware();
            }
        }

        private void dataGridViewHardware_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && dataGridViewHardware.SelectedRows.Count > 0)
            {
                RemoveHardware();
            }
        }

        private void dataGridViewHardware_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            e.Control.KeyPress -= new KeyPressEventHandler(IOColumn_KeyPress);
            if (dataGridViewHardware.CurrentCell.ColumnIndex == 1)
            {
                if (e.Control is TextBox tb)
                {
                    tb.KeyPress += new KeyPressEventHandler(IOColumn_KeyPress);
                }
            }
        }

        private void IOColumn_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }
        
        #region Button Events
        private void btnSelect_Click(object sender, EventArgs e)
        {
            var pathSave = new FolderBrowserDialog();

            if (pathSave.ShowDialog() == DialogResult.OK)
            {
                savePath = pathSave.SelectedPath;
                txtSavePath.Text = savePath;
            }

            pathSave.Dispose();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            AddHardware();
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            RemoveHardware();
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            string fileName = txtFileName.Text + ".cfg";
            string savePath = txtSavePath.Text;
            var rows = dataGridViewHardware.Rows;

            if (CanGenerate(fileName, savePath, rows))
            {
                string filePath = Path.Combine(savePath, fileName);
                string pcBasePath = Path.Combine(libraryFolder, "PC_Based.cfg");
                if(!File.Exists(pcBasePath))
                {
                    MessageBox.Show("File \"PC_Based.cfg\" not found in Library folder", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string[] pcBase = File.ReadAllLines(pcBasePath);
                pcBase = pcBase.Select(x => x.Replace("%currdate", DateTime.Now.ToString())).ToArray();

                List<string[]> componentsCfgFiles = new List<string[]>();
                foreach (DataGridViewRow row in dataGridViewHardware.Rows)
                {
                    if (row.Cells[0].Value != null)
                    {
                        string nameCol = row.Cells[0].Value.ToString();
                        string ioCol = row.Cells[1].Value.ToString();
                        string tag = row.Tag.ToString();
                        string formatedName = nameCol.ToLower().Replace(" ", "-");

                        if (!string.IsNullOrEmpty(GetFileByName(formatedName)))
                        {
                            string[] lines = ReplaceIOAddress(formatedName, tag, ioCol);
                            componentsCfgFiles.Add(lines);
                        }
                    }
                }

                File.WriteAllLines(filePath, pcBase);
                File.AppendAllText(filePath, "\n");
                foreach (var item in componentsCfgFiles)
                {
                    File.AppendAllLines(filePath, item);
                    File.AppendAllText(filePath, "\n");
                }

                MessageBox.Show("File Successfully Generated to Path: \n\"" + filePath + "\"", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        #endregion

        #endregion

        #region Private
        private void PopulateTree()
        {
            TreeNode tn = new TreeNode("Hardware");

            foreach (var file in Directory.GetFiles(libraryComponents))
            {
                if(Path.GetExtension(file).Contains("cfg"))
                {
                    string io = GetIOAddress(file);
                    string fileName = Path.GetFileNameWithoutExtension(file).ToUpper();
                    fileName = fileName.Replace("-", " ");
                    TreeNode comp = new TreeNode
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Tag = io,
                        Text = fileName
                    };
                    treeNodes.Add(comp);
                    tn.Nodes.Add(comp);
                }
            }

            treeViewHardware.Nodes.Add(tn);
            treeViewHardware.ExpandAll();
        }
        
        private string GetFileByName(string s)
        {
            return Directory.GetFiles(libraryComponents).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(s));
        }

        private void DisableControls(Control con)
        {
            foreach (Control c in con.Controls)
            {
                DisableControls(c);
            }
            con.Enabled = false;
        }
        
        private bool CanGenerate(string fileName, string savePath, DataGridViewRowCollection rows)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(savePath))
                return false;

            if (!IsValidFilename(fileName))
            {
                MessageBox.Show("Invalid file name", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            List<string> IOs = new List<string>();

            if (rows.Count > 0)
            {
                for (int i = 0; i < rows.Count-1; i++)
                {
                    if (rows[i].Cells[0].Value == null)
                        return false;

                    if (rows[i].Cells[1].Value != null)
                        IOs.Add(rows[i].Cells[1].Value.ToString());
                }
            }
            else
                return false;

            if (IOs.Count != IOs.Distinct().Count())
            {
                MessageBox.Show("IO Addresses must be different", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }
        
        private void AddHardware()
        {
            var selected = treeViewHardware.SelectedNode;
            if (selected != null && !selected.Text.Equals("Hardware"))
            {
                int index = dataGridViewHardware.Rows.Add(selected.Text, selected.Tag);
                dataGridViewHardware.Rows[index].Tag = selected.Tag;
            }
        }
        
        private void RemoveHardware()
        {
            var selectedRows = dataGridViewHardware.SelectedRows;
            if (selectedRows.Count > 0)
            {
                var selectedRow = selectedRows[0];
                if ((selectedRow.Index + 1) != dataGridViewHardware.Rows.Count) // is not last row
                {
                    dataGridViewHardware.Rows.Remove(selectedRow);
                }
            }
        }
        
        private string GetIOAddress(string path)
        {
            string io = "";

            string[] lines = File.ReadAllLines(path);
            foreach (var line in lines)
            {
                if(line.ToLower().Contains("ioaddress"))
                {
                    string[] substring = line.Split(',');
                    foreach (var s in substring)
                    {
                        if(s.ToLower().Contains("ioaddress"))
                        {
                            io = s.ToLower().Replace("ioaddress", string.Empty).Replace(" ", string.Empty);
                            break;
                        }
                    }
                    break;
                }
            }

            return io;
        }
        
        private string[] ReplaceIOAddress(string fileName, string oldIO, string newIO)
        {
            string[] lines = File.ReadAllLines(Path.Combine(libraryComponents, fileName + ".cfg"));
            lines = lines.Select(x => x.Replace("IOADDRESS " + oldIO, "IOADDRESS " + newIO)).ToArray();
            return lines;
        }

        private bool IsValidFilename(string filename)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                if (filename.Contains(ch))
                    return false;
            }

            return true;
        }
        #endregion
    }
}

