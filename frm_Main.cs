﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;

namespace BulkImportDelimitedFlatFiles
{

    public partial class frm_Main : Form
    {


        SqlConnection cnn = new SqlConnection();
        int hasError = 0;
        FileInfo[] files = default(FileInfo[]);
        Dictionary<string, DataTable> filesToLoad = new Dictionary<string, DataTable>();
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> filesToLoadMapping = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        Boolean fileListLoaded = false;
        LocalConfig lconfig;
        Thread mainThread;

        public frm_Main()
        {
            InitializeComponent();
            lbl_ExpandTopPanel.Visible = false;
            cmbox_delimiter.SelectedItem = "Tab";
            txtbox_defaultDataLength.Text = "max";
            dgv_FieldList.Enabled = false;
            this.setJsonObjectConfig();
            btn_loadToSQL.Enabled = false;
            this.mainThread = Thread.CurrentThread;
            this.mainThread.Name = "Main Thread";
        }

        private void setJsonObjectConfig()
        {
            var enviroment = System.Environment.CurrentDirectory;
            if (File.Exists(@"" + Directory.GetParent(enviroment).Parent.FullName.ToString().Replace(".dll", "") + @"\config.json"))
            {
                this.lconfig = JsonSerializer.Deserialize<LocalConfig>(File.ReadAllText(@"" + Directory.GetParent(enviroment).Parent.FullName.ToString().Replace(".dll", "") + @"\config.json"));
            }
            txtbox_FinalTableName.Text = this.lconfig.tableName.ToString();
            txtbox_tablePrefix.Text = this.lconfig.tablePrefix.ToString();

        }


        private void btn_testConnection_Click(object sender, EventArgs e)
        {
            string cnnString;
            string sqlServer;
            string sqlUser;
            string sqlPass;
            string sqlDatabase;

            // set sql variables
            sqlServer = txtbox_sqlServer.Text.ToString(); sqlUser = txtbox_sqlUser.Text.ToString(); sqlPass = txtbox_sqlPass.Text.ToString(); sqlDatabase = txtbox_sqlDatabase.Text.ToString();

            if (chbox_windowsAuth.Checked == true)
            {
                if (txtbox_sqlServer.Text.ToString() == "" || txtbox_sqlDatabase.Text.ToString() == "")
                {
                    MessageBox.Show("I am sorry, but you need to make sure you fill out ALL of the SQL Server Connection Info Boxes");
                    this.hasError = 1;
                    return;
                }
                cnnString = "Data Source=" + sqlServer + ";Initial Catalog=\"" + sqlDatabase + "\";Integrated Security=true;";
            }
            else
            {
                if (txtbox_sqlServer.Text.ToString() == "" || txtbox_sqlUser.Text.ToString() == "" || txtbox_sqlPass.Text.ToString() == "" || txtbox_sqlDatabase.Text.ToString() == "")
                {
                    MessageBox.Show("I am sorry, but you need to make sure you fill out ALL of the SQL Server Connection Info Boxes");
                    this.hasError = 1;
                    return;
                }
                cnnString = "Data Source=" + sqlServer + ";Initial Catalog=" + sqlDatabase + ";User ID=" + sqlUser + ";Password=" + sqlPass + "";
            }






            if (this.cnn.State == ConnectionState.Open)
            {
                this.cnn.Close();
            }
            this.cnn.ConnectionString = cnnString;
            lbl_testConnStatus.ForeColor = Color.Black;
            lbl_testConnStatus.Text = "Trying to Connect...";
            try
            {
                this.cnn.Open();
                lbl_testConnStatus.ForeColor = Color.Green;
                lbl_testConnStatus.Text = "Connection Successful";
                this.cnn.Close();

            }
            catch (Exception err)
            {
                lbl_testConnStatus.ForeColor = Color.Red;
                lbl_testConnStatus.Text = err.Message.ToString();
                this.hasError = 1;
                return;
            }

        }

        private void btn_openFileDiag_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            DialogResult dr = fd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                txtbox_pickUpPath.Text = fd.SelectedPath.ToString();
            }

        }

        public void flComplete(object sender, EventArgs e)
        {
            this.fileListLoaded = true;

            foreach (ListViewItem lv in lv_fileList.Items)
            {
                this.updateFileMapping(sender, e, lv.Index);
                lv_fileList.Items[lv.Index].Checked = true;
            }
            if (lv_fileList.Items[0] != null)
            {
                lv_fileList.Items[0].Selected = true;
                lv_fileList.Items[0].Focused = true;
            }
            btn_loadFilesToList.Enabled = true;
            btn_loadFilesToList.Text = "Load Files";
            dgv_FieldList.Enabled = true;
            lv_fileList.Enabled = true;
            btn_loadToSQL.Enabled = true;
        }

        public void ldFiles(DirectoryInfo di, object sender, EventArgs e)
        {
            //MessageBox.Show("Made it to the func");
            try
            {

                string[] extensions = new[] { ".txt", ".csv" };

                // CLEAR Files If the list is not empty
                Invoke(new Action(clearFileList));
                Invoke(new Action(clearFileList));

                // create local thread object to store the files
                FileInfo[] tfiles;
                tfiles = di.EnumerateFiles().Where(f => extensions.Contains(f.Extension.ToLower())).ToArray();

                // Update parent object with the file list from the child thread -- this is safe because it should only be ran once due to button being disabled
                Invoke(new Action<FileInfo[]>(updateFileListObject), new object[] { tfiles });



                // Create new local thread objec to hold files to load
                Dictionary<string, DataTable> ftLoad = new Dictionary<string, DataTable>();
                Invoke(new Action(updateFilesToLoad));
                List<string> failedFiles = new List<string>();
                foreach (FileInfo fl in tfiles)
                {
                    string fileName = fl.FullName.ToString();
                    //MessageBox.Show(fileName);
                    Invoke(new Action<string>(addFileToList), fileName);

                    DataTable dtfl = (DataTable)Invoke(new Func<string, DataTable>(GetDataTableFromCSVFile), fileName);

                    if (dtfl != null)
                    {
                        Invoke(new Action<string, DataTable>(addAFileToLoad), new object[] { fileName, dtfl });
                    }
                    else
                    {
                        Invoke(new Action<string>(addFailedFileToList), fileName);
                    }
                }
                Invoke(new Action<object, EventArgs>(flComplete), new object[] { sender, e });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message.ToString());
                Invoke(new Action(updateButtonsAfterLoadFiles));
            }
        }
        public void addAFileToLoad(string file, DataTable dt)
        {
            this.filesToLoad.Add(file, dt);
        }
        public void addAFailedFileToLoad(string file, DataTable dt)
        {
            this.filesToLoad.Add(file, dt);
        }

        public void updateButtonsAfterLoadFiles()
        {
            btn_loadFilesToList.Enabled = true;
            dgv_FieldList.Enabled = false;
        }

        public void updateFilesToLoad()
        {
            if (filesToLoad != null)
            {
                filesToLoad.Clear();
            }
        }

        public void updateFileListObject(FileInfo[] fltu)
        {
            files = fltu;
        }
        public void clearFileList()
        {
            if (files != null)
            {
                Array.Clear(files, 0, files.Length);
            }
        }

        public void clearLVFiles()
        {
            lv_fileList.Items.Clear();
        }
        public void addFileToList(string file)
        {
            lv_fileList.Items.Add(file);
        }

        public void addFailedFileToList(string file)
        {
            lv_fileList.Items.Add(file).ForeColor = Color.Red;
        }

        private void btn_loadFilesToList_Click(object sender, EventArgs e)
        {
            /*
             *  VALIDATION STUFF
             */
            lv_fileList.Items.Clear();
            dgv_FieldList.Rows.Clear();
            //this.btn_testConnection_Click(sender, e);
            if (this.hasError == 1) { return; }
            if (txtbox_pickUpPath.Text.ToString() == "" || Directory.Exists(txtbox_pickUpPath.Text.ToString()) == false)
            {
                MessageBox.Show("I am sorry your folder path is either blank or does not exist");
                this.hasError = 1;
            }
            if (this.hasError == 1) { return; }
            var filesNullOrNot = (files != null);
            var filesToLoadIsNull = (filesToLoad != null);


            btn_loadFilesToList.Enabled = false;
            btn_loadFilesToList.Text = "Loading...";

            Thread lfThread = new Thread(() => ldFiles(new DirectoryInfo(txtbox_pickUpPath.Text.ToString()), sender, e));
            lfThread.Start();
            //Task.Factory.StartNew(/*(*/) => );
            // public void ldFiles (string puPath,FileInfo[] tfiles, ListView lvfiles, Dictionary<string, DataTable> ftLoad, DataGridView dgvfl )
        }

        // TODO: Need to add Checks for Drop Table and for Create Unioned Table
        // TODO: Need to add more validation
        private void loadTablesToSQLServer()
        {


            if (txtbox_FinalTableName.Text.Trim() == "" && chbox_CreateAllTablesTable.Checked == true)
            {
                MessageBox.Show("Sorry you have chosen to create an all tables and left the final table name blank. Please fill something in.");
                return;
            }
            int fCount = this.lv_fileList.CheckedItems.Count;
            int fCnt = 1;
            if (fCount == 0)
            {
                MessageBox.Show("Sorry but you have not selected any files to Load. Please select the check box next to the files you wish to load.");
                return;
            }
            string lastTableName;
            int lastTableNumberOriginal = 1;
            int lastTableNumber = 1;
            string tablePrefix = "";
            Boolean useTablePrefix = chbox_tablePrefix.Checked;


            decimal currentProg = (decimal)((double)fCnt / fCount * 100);

            if (useTablePrefix)
            {
                tablePrefix = txtbox_tablePrefix.Text.ToString();
                try
                {
                    //string query = @"SELECT MAX(t.name) AS LastTable, RIGHT(MAX(T.name),3) AS LastTableNumber FROM sys.tables t WHERE name LIKE '%" + tablePrefix + @"[0-9]%'";
                    string query = @"SELECT * FROM (SELECT MAX(t.name) AS LastTable, RIGHT(MAX(T.name),3) AS LastTableNumber FROM sys.tables t WHERE name LIKE '" + tablePrefix + @"[0-9]%') a WHERE lastTable IS NOT null";
                    cnn.Open();
                    SqlCommand cmd = new SqlCommand(query, this.cnn);

                    //MessageBox.Show("Connection Open ! ");
                    SqlDataAdapter da = new SqlDataAdapter(cmd);
                    DataSet ds = new DataSet();
                    da.Fill(ds);

                    if (ds.Tables.Count > 0)
                    {
                        if (ds.Tables[0].Rows.Count > 0)
                        {
                            lastTableName = ds.Tables[0].Rows[0]["LastTable"].ToString();
                            lastTableNumberOriginal = Int32.Parse(ds.Tables[0].Rows[0]["LastTableNumber"].ToString()) + 1;
                        }
                        else
                        {
                            lastTableName = txtbox_tablePrefix.Text.ToString();
                            lastTableNumberOriginal = Int32.Parse("000") + 1;
                        }
                    }
                    else
                    {
                        lastTableName = txtbox_tablePrefix.Text.ToString();
                        lastTableNumberOriginal = Int32.Parse("000") + 1;
                    }
                    this.cnn.Close();
                }
                catch (Exception ex)
                {
                    if (this.cnn.State == ConnectionState.Open) { cnn.Close(); }
                    MessageBox.Show(ex.Message.ToString());
                    return;
                }
            }
            lastTableNumber = lastTableNumberOriginal;
            frm_loadStatus ltDialog = new frm_loadStatus(txtbox_sqlServer.Text.ToString());
            ltDialog.Show();
            Boolean errorsHappened = false;
            foreach (ListViewItem lii in lv_fileList.CheckedItems)
            {

                lbl_loadFilesStatus.ForeColor = Color.Black;
                lbl_loadFilesStatus.Text = "Loading your files...";
                DataTable dtToLoad;
                FileInfo fi = FileSystem.GetFileInfo(lii.Text.ToString());
                string fName = fi.Name.ToString().Replace(".txt", "").Replace(".csv", "");
                string tNameToInsert = useTablePrefix ? tablePrefix + lastTableNumber.ToString().PadLeft(3, '0') : fName;
                ltDialog.setLoadingText("Loading Table " + tNameToInsert);
                ltDialog.setLoadStatus(currentProg);

                try
                {

                    dtToLoad = this.filesToLoad[lii.Text.ToString()];
                    if (chbox_DropTables.Checked == true)
                    {
                        string dropTable = "if exists(select 1 FROM sys.tables where name = '" + tNameToInsert + "') BEGIN DROP TABLE [" + tNameToInsert + "]; END";
                        SqlCommand cmdDT = new SqlCommand(dropTable, this.cnn);
                        cmdDT.Connection.Open(); cmdDT.ExecuteNonQuery(); cmdDT.Connection.Close();
                        cmdDT.Dispose();
                    }


                    string createTable = "CREATE TABLE [" + tNameToInsert + "] (";
                    Dictionary<string, Dictionary<string, string>> fd = this.filesToLoadMapping[fi.FullName.ToString()];

                    int cLength = dtToLoad.Columns.Count;
                    for (int i = 0; i < cLength; i++)
                    {
                        Dictionary<string, string> dd = fd[dtToLoad.Columns[i].Ordinal.ToString()];
                        string dtstr = dd["dataType"].ToString() == "Text" ? "varchar" : dd["dataType"].ToString();
                        if (dd["dataType"].ToString() == "Text")
                        {
                            dtstr += "(" + dd["dtLength"].ToString() + ")";
                        }
                        //this.filesToLoadMapping[dtToLoad.Columns[i].ToString()]

                        if (i == cLength - 1)
                        {
                            createTable += "[" + dtToLoad.Columns[i].ToString() + "] " + dtstr;
                        }
                        else
                        {
                            createTable += "[" + dtToLoad.Columns[i].ToString() + "]  " + dtstr + ",";
                        }
                    }
                    createTable += ") ";
                    System.Diagnostics.Debug.WriteLine(createTable);
                    SqlCommand cmd2 = new SqlCommand(createTable, this.cnn);
                    cmd2.Connection.Open(); cmd2.ExecuteNonQuery(); cmd2.Connection.Close();
                    cmd2.Dispose();

                    this.cnn.Open();
                    using (SqlBulkCopy s = new SqlBulkCopy(cnn))
                    {
                        string abc = "dbo.[" + tNameToInsert + "]";
                        s.DestinationTableName = abc;
                        s.BulkCopyTimeout = 0;
                        s.WriteToServer(dtToLoad);
                    }
                    this.cnn.Close();
                    lbl_loadFilesStatus.ForeColor = Color.Green;
                    lbl_loadFilesStatus.Text = "Your files have been loaded";

                    //InsertDataIntoSQLServerUsingSQLBulkCopy(dtToLoad, tToInsert, cnn);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message.ToString());
                    cnn.Close();
                    lbl_loadFilesStatus.ForeColor = Color.Red;
                    lbl_loadFilesStatus.Text = ex.Message;
                    errorsHappened = true;
                }
                fCnt++;
                lastTableNumber++;
                currentProg = (decimal)((double)fCnt / fCount * 100);
            }



            if (chbox_CreateAllTablesTable.Checked == false)
            {
                ltDialog.setLoadingText(errorsHappened ? @"We loaded the tables we could" : @"Your Tables have Been Loaded ");
                ltDialog.setLoadStatus(new decimal(100));
                ltDialog.showOkButton();
                btn_loadToSQL.Enabled = false;
                return;
            }

            lastTableNumber = lastTableNumberOriginal;
            //foreach (System.Collections.Generic.KeyValuePair<string, DataTable> fl in this.filesToLoad) {


            //} // end foreach


            if (chbox_CreateAllTablesTable.Checked == true)
            {
                string finalTable = txtbox_FinalTableName.Text.ToString();
                try
                {
                    decimal lstat = new decimal(90);
                    ltDialog.setLoadStatus(lstat);
                    ltDialog.setLoadingText("Trying to create your Unioned Table \"" + txtbox_FinalTableName.Text.ToString() + " \"");

                    if (chbox_DropTables.Checked == true)
                    {
                        string dropTable = "if exists(select 1 FROM sys.tables where name = '" + finalTable + "') BEGIN DROP TABLE [" + finalTable + "]; END";
                        SqlCommand cmdDT = new SqlCommand(dropTable, this.cnn);
                        cmdDT.Connection.Open(); cmdDT.ExecuteNonQuery(); cmdDT.Connection.Close();
                        cmdDT.Dispose();
                    }

                    cnn.Open();
                    var enviroment = System.Environment.CurrentDirectory;
                    string currentLocationOfExe = Directory.GetParent(enviroment).Parent.FullName;

                    string script = File.ReadAllText(@"" + currentLocationOfExe.ToString().Replace(".dll", "") + @"\SQL Scripts\DropOriginalTableCustomFunctions.sql");
                    SqlCommand cmScripts = new SqlCommand(script, cnn);
                    cmScripts.ExecuteNonQuery();
                    script = File.ReadAllText(@"" + currentLocationOfExe.ToString().Replace(".dll", "") + @"\SQL Scripts\OriginalTableList.sql");
                    cmScripts.CommandText = script;
                    cmScripts.ExecuteNonQuery();
                    script = File.ReadAllText(@"" + currentLocationOfExe.ToString().Replace(".dll", "") + @"\SQL Scripts\get_originalUnion.sql");
                    cmScripts.CommandText = script;
                    cmScripts.ExecuteNonQuery();
                    cnn.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message.ToString());
                    cnn.Close();
                    return;
                }



                Dictionary<string, string> tListLoaded = new Dictionary<string, string>();
                try
                {

                    string qToUnion = @"
                    DECLARE @tvalues OriginalTableList ";
                    qToUnion += useTablePrefix ? @"
                    INSERT INTO @tvalues (tbl, srcFile) VALUES " : @"
                    INSERT INTO @tvalues(tbl) VALUES ";
                    int itemsAddedCount = this.lv_fileList.CheckedItems.Count;
                    int itemsAddedCounter = 1;
                    foreach (ListViewItem lii in this.lv_fileList.CheckedItems)
                    {
                        FileInfo fl = FileSystem.GetFileInfo(lii.Text.ToString());
                        string fName = fl.Name.ToString().Replace(".txt", "").Replace(".csv", "");
                        string tName = useTablePrefix == true ? tablePrefix + lastTableNumber.ToString().PadLeft(3, '0') : fName;
                        if (itemsAddedCounter != itemsAddedCount)
                        {
                            qToUnion +=
                                useTablePrefix ? @" ('" + tName + @"', '" + fName + "'), "
                                : @" ('" + fName + @"'), ";

                        }
                        else
                        {
                            qToUnion +=
                            useTablePrefix ? @" ('" + tName + @"', '" + fName + "') "
                            : @" ('" + fName + @"') ";
                        }
                        if (useTablePrefix)
                        {
                            tListLoaded.Add(fl.FullName.ToString(), tName);
                        }
                        lastTableNumber++;
                        itemsAddedCounter++;
                    }



                    qToUnion += Environment.NewLine;
                    string nTName = finalTable;
                    qToUnion += @"EXEC dbo.get_OriginalUnion @tvalues, '" + nTName + "'";

                    SqlCommand cmd3 = new SqlCommand(qToUnion, cnn);
                    cmd3.Connection.Open(); cmd3.ExecuteNonQuery(); cmd3.Connection.Close();
                    cmd3.Dispose();
                    //string ChangeOriginalTableToFile = @"EXEC sp_rename 'dbo."+finalTable+".OriginalTable', 'OriginalFile', 'COLUMN'; ";
                    //SqlCommand cmd4 = new SqlCommand(ChangeOriginalTableToFile, cnn);
                    //cmd4.Connection.Open(); cmd4.ExecuteNonQuery(); cmd4.Connection.Close();
                    //cmd4.Dispose();

                    ltDialog.setLoadingText(@"You Now Have A New Table " + Environment.NewLine + nTName);
                    ltDialog.setLoadStatus(new decimal(100));
                    ltDialog.showOkButton();
                }
                catch (Exception em)
                {
                    MessageBox.Show(em.Message.ToString());
                    ltDialog.showCancelButton();
                }

            }
            // SETUP Scripts So that SQL Can be updated for union table and used to create Union Table
            btn_loadToSQL.Enabled = false;
        }

        private DataTable GetDataTableFromCSVFile(string csv_file_path)
        {

            DataTable csvData = new DataTable();
            try
            {
                using (TextFieldParser csvReader = new TextFieldParser(csv_file_path))
                {
                    string[] dv;
                    List<string> dl = new List<string>();
                    switch (cmbox_delimiter.Text.ToString())
                    {
                        case "Tab":
                            dl.Add("\t");
                            break;
                        case "Comma":
                            dl.Add(",");
                            break;
                        case "Semi-Colon":
                            dl.Add(";");
                            break;
                        case "Pipe":
                            dl.Add("|");
                            break;
                        default:
                            dl.Add("\t");
                            break;
                    }
                    dv = dl.ToArray();
                    //MessageBox.Show(dv);
                    csvReader.SetDelimiters(dv);
                    csvReader.HasFieldsEnclosedInQuotes = true;
                    string[] colFields = csvReader.ReadFields();
                    foreach (string column in colFields)
                    {
                        DataColumn datecolumn = new DataColumn(column);
                        datecolumn.AllowDBNull = true;
                        csvData.Columns.Add(datecolumn);
                    }
                    while (!csvReader.EndOfData)
                    {
                        string[] fieldData = csvReader.ReadFields();
                        //Making empty value as null
                        for (int i = 0; i < fieldData.Length; i++)
                        {
                            if (fieldData[i] == "")
                            {
                                fieldData[i] = null;
                            }
                        }
                        csvData.Rows.Add(fieldData);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message.ToString());
                return null;
            }
            return csvData;
        }

        private void splitContainer1_Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void lbl_CollapseTopPanel_Click(object sender, EventArgs e)
        {
            sc_Main.Panel1Collapsed = true;
            lbl_ExpandTopPanel.Visible = true;
        }

        private void lbl_ExpandTopPanel_Click(object sender, EventArgs e)
        {
            sc_Main.Panel1Collapsed = false;
            lbl_ExpandTopPanel.Visible = false;
        }

        private void tsm_exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void tsm_clearData_Click(object sender, EventArgs e)
        {
            string msg = "Are you sure you want to clear all the data on screen?";
            var result = MessageBox.Show(msg, "Clear Data", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            // If the no button was pressed ...
            if (result == DialogResult.OK)
            {
                // clear the screens data
                this.clearTheScreen();

            }
        }

        private void clearTheScreen()
        {
            // Clear SQL Server Stuff
            // CheckBoxes
            txtbox_sqlServer.Text = "";
            txtbox_sqlUser.Text = "";
            txtbox_sqlPass.Text = "";
            txtbox_sqlDatabase.Text = "";
            txtbox_pickUpPath.Text = "";

            // Labels
            lbl_testConnStatus.Text = "";

            // Clear bottom Checkboxes
            chbox_DropTables.Checked = false;
            chbox_CreateAllTablesTable.Checked = true;

            // Clear lists and tables
            lv_fileList.Items.Clear();
            dgv_FieldList.Rows.Clear();

            // Clear Other Labels
            lbl_LoadToSQLStatus.Text = "";
            lbl_loadFilesStatus.Text = "";

        }

        private void chbox_windowsAuth_CheckedChanged(object sender, EventArgs e)
        {
            if (this.chbox_windowsAuth.Checked == true)
            {
                txtbox_sqlUser.Enabled = false;
                txtbox_sqlPass.Enabled = false;
            }
            else
            {
                txtbox_sqlUser.Enabled = true;
                txtbox_sqlPass.Enabled = true;
            }
        }
        private void updateFileMapping(object sender, EventArgs e, int idx)
        {

            dgv_FieldList.Rows.Clear();
            int ci = idx == -1 ? lv_fileList.SelectedIndex() : idx;
            FileInfo fl = this.files[ci];
            if (!this.filesToLoad.ContainsKey(fl.FullName.ToString()))
            {
                return;
            }
            DataTable dt = this.filesToLoad[fl.FullName.ToString()];
            Dictionary<string, Dictionary<string, string>> ftm = null;
            if (this.filesToLoadMapping.ContainsKey(fl.FullName.ToString()))
            {
                ftm = this.filesToLoadMapping[fl.FullName.ToString()];
            }

            if (ftm == null)
            {
                // Mapping Doesn't exist prefil will generic mapping
                foreach (DataColumn col in dt.Columns)
                {
                    dgv_FieldList.Rows.Add(
                        col.Ordinal.ToString()
                        , col.ColumnName.ToString()
                        , "Text"
                        , txtbox_defaultDataLength.Text.ToString()
                    );
                }
                this.dgv_FieldList_CellValueChanged_Call(sender, null, ci);
            }
            else
            {
                // Mapping Does Exist. Get Mapping and prefill with mapping
                foreach (System.Collections.Generic.KeyValuePair<string, Dictionary<string, string>> em in ftm)
                {
                    dgv_FieldList.Rows.Add(
                        em.Value["Ordinal"].ToString()
                        , em.Value["fieldName"].ToString()
                        , em.Value["dataType"]
                        , em.Value["dtLength"]
                    );
                }
            }
        }

        private void cbl_fileList_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.updateFileMapping(sender, e, -1);
        }

        private void dgv_FieldList_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            this.dgv_FieldList_CellValueChanged_Call(sender, e, -1);
        }
        private void updateMappingForFile(int idx)
        {
            if (this.files != null)
            {
                FileInfo fl = this.files[idx];
                this.filesToLoadMapping.Remove(fl.FullName.ToString());
                Dictionary<string, Dictionary<string, string>> ml = new Dictionary<string, Dictionary<string, string>>();
                foreach (DataGridViewRow r in dgv_FieldList.Rows)
                {
                    Dictionary<string, string> dmr = new Dictionary<string, string>();

                    dmr.Add("Ordinal", r.Cells["index"].Value.ToString());
                    dmr.Add("fieldName", r.Cells["fieldName"].Value.ToString());
                    dmr.Add("dataType", r.Cells["dataType"].Value.ToString());
                    dmr.Add("dtLength", r.Cells["dtLength"].Value.ToString());
                    ml.Add(r.Cells["index"].Value.ToString(), dmr);
                }
                this.filesToLoadMapping.Add(fl.FullName.ToString(), ml);

            }
        }
        private void dgv_FieldList_CellValueChanged_Call(object sender, DataGridViewCellEventArgs e, int ci)
        {
            int idx = ci == -1 ? lv_fileList.SelectedIndex() : ci;
            if (this.fileListLoaded)
            {
                if (e == null)
                {
                    this.updateMappingForFile(idx);
                    return;
                }
                if (e.RowIndex != 0)
                {
                    int ri = e.RowIndex;
                    //MessageBox.Show(ri.ToString());
                    DataGridViewRow dgvr = dgv_FieldList.Rows[e.RowIndex];
                    string input = dgvr.Cells["dtLength"].Value.ToString();
                    string patt1 = @"\d+";
                    Match m1 = Regex.Match(input, patt1);
                    Boolean mm1;
                    if (m1.Length > 0) { mm1 = true; } else { mm1 = false; }
                    if (
                            dgvr.Cells["dataType"].Value.ToString() == "Text"
                            && input.ToUpper() != "MAX"
                            && !mm1
                        )
                    {
                        MessageBox.Show("Your Value is invalid for the data length");
                        dgvr.Cells["dtLength"].Value = "max";
                    }
                    this.updateMappingForFile(idx);
                }

            }






        }



        private void btn_CheckStuff_Click(object sender, EventArgs e)
        {
            string opt = "";
            foreach (System.Collections.Generic.KeyValuePair<string, System.Data.DataTable> fl in this.filesToLoad)
            {
                opt += fl.Key.ToString() + "\n";
                Dictionary<string, Dictionary<string, string>> fm = this.filesToLoadMapping[fl.Key.ToString()];
                foreach (System.Collections.Generic.KeyValuePair<string, Dictionary<string, string>> fmr in fm)
                {
                    foreach (System.Collections.Generic.KeyValuePair<string, string> fmrv in fmr.Value)
                    {
                        opt += fmrv.Key.ToString() + " >>> " + fmrv.Value.ToString() + "\n";
                    }

                }
            }
            MessageBox.Show(opt);
        }

        private void btn_loadToSQL_Click(object sender, EventArgs e)
        {
            this.btn_testConnection_Click(sender, e);
            this.loadTablesToSQLServer();
        }

        private void chbox_CreateAllTablesTable_CheckedChanged(object sender, EventArgs e)
        {
            if (this.chbox_CreateAllTablesTable.Checked)
            {
                txtbox_FinalTableName.Visible = true;
            }
            else
            {
                txtbox_FinalTableName.Visible = false;
            }
        }

        private void openExcelToCSv(object sender, EventArgs e)
        {
            frm_excelToCSV etc = new frm_excelToCSV();
            etc.Show();
        }

        private void chbox_tablePrefix_CheckedChanged(object sender, EventArgs e)
        {
            if (chbox_tablePrefix.Checked)
            {
                txtbox_tablePrefix.Visible = true;
            }
            else
            {
                txtbox_tablePrefix.Visible = false;
            }
            if (chbox_tablePrefix.Checked && chbox_DropTables.Checked)
            {
                lbl_warningIncremental.Text = "Drop Tables will be ignored w/create incremental checked";
                lbl_warningIncremental.ForeColor = Color.Red;
            }
            else
            {
                lbl_warningIncremental.Text = "";
            }
        }

        private void chbox_DropTables_CheckedChanged(object sender, EventArgs e)
        {
            if (chbox_tablePrefix.Checked && chbox_DropTables.Checked)
            {
                lbl_warningIncremental.Text = "Drop Tables will be ignored w/create incremental checked";
                lbl_warningIncremental.ForeColor = Color.Red;
            }
            else
            {
                lbl_warningIncremental.Text = "";
            }
        }
    }
    public static class Extension
    {
        public static int SelectedIndex(this ListView listView)
        {
            if (listView.SelectedIndices.Count > 0)
                return listView.SelectedIndices[0];
            else
                return 0;
        }
    }

    public class LocalConfig
    {
        public string tableName { get; set; }
        public string tablePrefix { get; set; }
    }
}
