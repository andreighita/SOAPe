﻿/*
 * By David Barrett, Microsoft Ltd. 2013. Use at your own risk.  No warranties are given.
 * 
 * DISCLAIMER:
 * THIS CODE IS SAMPLE CODE. THESE SAMPLES ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND.
 * MICROSOFT FURTHER DISCLAIMS ALL IMPLIED WARRANTIES INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OF MERCHANTABILITY OR OF FITNESS FOR
 * A PARTICULAR PURPOSE. THE ENTIRE RISK ARISING OUT OF THE USE OR PERFORMANCE OF THE SAMPLES REMAINS WITH YOU. IN NO EVENT SHALL
 * MICROSOFT OR ITS SUPPLIERS BE LIABLE FOR ANY DAMAGES WHATSOEVER (INCLUDING, WITHOUT LIMITATION, DAMAGES FOR LOSS OF BUSINESS PROFITS,
 * BUSINESS INTERRUPTION, LOSS OF BUSINESS INFORMATION, OR OTHER PECUNIARY LOSS) ARISING OUT OF THE USE OF OR INABILITY TO USE THE
 * SAMPLES, EVEN IF MICROSOFT HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES. BECAUSE SOME STATES DO NOT ALLOW THE EXCLUSION OR LIMITATION
 * OF LIABILITY FOR CONSEQUENTIAL OR INCIDENTAL DAMAGES, THE ABOVE LIMITATION MAY NOT APPLY TO YOU.
 * */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.IO;

namespace SOAPe
{
    public partial class FormReplaceTemplateFields : Form
    {
        private string _xml = "";
        private static string _templatePath = "";
        private string _currentTemplateName = "";
        private string _templateXML = "";
        private Form _owner = null;
        private string[] _distinguishedFolders = {"calendar","contacts","deleteditems","drafts","inbox","journal","notes","outbox","sentitems",
                                                     "tasks","msgfolderroot","publicfoldersroot","root", "junkemail", "searchfolders","voicemail","recoverableitemsroot",
                                                     "recoverableitemsdeletions", "recoverableitemsversions","recoverableitemspurges","archiveroot",
                                                     "archivemsgfolderroot","archivedeleteditems","archiverecoverableitemsroot",
                                                     "archiverecoverableitemsdeletions","archiverecoverableitemsversions",
                                                     "archiverecoverableitemspurges","syncissues","conflicts","localfailures","serverfailures","receipientcache",
                                                     "quickcontacts","conversationhistory","todosearch"};
        private Dictionary<string, string> _fieldType;
        private bool _cancel = false;
        private string _itemId = "";
        private string _folderId = "";

        public FormReplaceTemplateFields()
        {
            InitializeComponent();
            _fieldType = new Dictionary<string, string>();
            buttonLoad.Enabled = false;
            InitFolderCombo();
        }

        public FormReplaceTemplateFields(string ItemId, string FolderId=""): this()
        {
            _itemId = ItemId;
            _folderId = FolderId;
        }


        private void InitFolderCombo()
        {
            if (String.IsNullOrEmpty(_templatePath))
                ReadTemplatePath();

            // Read subfolders from template path
            comboBoxTemplateFolder.Items.Clear();
            foreach (DirectoryInfo dir in new DirectoryInfo(_templatePath).GetDirectories())
            {
                int i=comboBoxTemplateFolder.Items.Add(dir.Name);
                if (dir.Name.ToLower().Equals("ews"))
                    comboBoxTemplateFolder.SelectedIndex = i;
            }
            
        }

        private void ReadTemplatePath()
        {
            try
            {
                if (String.IsNullOrEmpty(_templatePath))
                {
                    // Try to set template path
                    _templatePath = Directory.GetCurrentDirectory();
                    string sPath = Path.GetDirectoryName(Application.ExecutablePath) + "\\Templates";
                    if (!Directory.Exists(sPath))
                    {
                        // In case we are running directly from bin folder, we'll try looking for files
                        // where they would be in development environment
                        sPath = Path.GetDirectoryName(Application.ExecutablePath);
                        sPath = Directory.GetParent(sPath).FullName;
                        sPath = Directory.GetParent(sPath).FullName + "\\Templates";
                    }
                    if (Directory.Exists(sPath))
                        _templatePath = sPath;
                }
                return;
            }
            catch { }
        }

        private void ReadAvailableTemplates()
        {
            // Read any XML templates available and add to templates combobox
            comboBoxTemplate.Items.Clear();
            string[] xmlFiles = null;
            string sPath = String.Format("{0}\\{1}", _templatePath, comboBoxTemplateFolder.Text);
            if (!Directory.Exists(sPath))
                return;

            try
            {
                xmlFiles = Directory.GetFiles(sPath, "*.xml");
            }
            catch { }

            if (xmlFiles == null) return;

            foreach (string sXMLFile in xmlFiles)
            {
                if (!sXMLFile.ToLower().EndsWith("customfieldtest.xml"))
                {
                    comboBoxTemplate.Items.Add(sXMLFile.Substring(sPath.Length + 1, sXMLFile.Length - 5 - sPath.Length));
                }
                else // We only show CustomFieldTest template when running from IDE (i.e. debugging)
                    if (System.Diagnostics.Debugger.IsAttached)
                        comboBoxTemplate.Items.Add(sXMLFile.Substring(sPath.Length + 1, sXMLFile.Length - 5 - sPath.Length));
            }
        }

        private string ReadTemplate()
        {
            string sPath = String.Format("{0}\\{1}", _templatePath, comboBoxTemplateFolder.Text);
            if (!Directory.Exists(sPath))
                return "";

            string sFile = String.Format("{0}\\{1}", sPath, comboBoxTemplate.Text);
            if (!File.Exists(sFile))
            {
                if (!sFile.EndsWith(".xml"))
                    sFile += ".xml";
                if (!File.Exists(sFile))
                    return "";
            }

            StreamReader oReader = null;
            try
            {
                oReader = new StreamReader(sFile);
                string sTemplateContent = oReader.ReadToEnd();
                _currentTemplateName = comboBoxTemplate.Text;
                return sTemplateContent;
            }
            catch
            {
                return "";
            }
            finally
            {
                oReader.Close();
            }
        }

        public string ReadTemplate(Form Owner)
        {
            _owner = Owner;
            _cancel = false;
            this.ShowDialog(Owner);
            if (_cancel) return null;
            ReplaceFields();
            return _xml;
        }

        public string ReplaceTemplateFields(string TemplateXML, Form Owner, bool remove)
        {
            // Replace any fields in the template as per the user configuration
            _templateXML = TemplateXML;
            _owner = Owner;
            if (!PopulateFieldList()) return TemplateXML;
            xmlEditor1.Text = _templateXML;
            UpdateXml();
            _cancel = false;
            this.ShowDialog(Owner);
            if (_cancel) return null;
            ReplaceFields();
            return _xml;
        }

        private string FieldType(string FieldName)
        {
            if (_fieldType.ContainsKey(FieldName))
                return _fieldType[FieldName];
            return String.Empty;
        }

        private void ReplaceFields()
        {
            // Replace the field placeholders with the values from the datagrid
            Regex oRegEx;

            _xml = _templateXML;
            string sId = "";
            for(int i=0;i<dataGridViewFields.Rows.Count; i++)
            {
                DataGridViewRow oRow = dataGridViewFields.Rows[i];
                string sFieldName = oRow.Cells[0].Value.ToString();
                string sFieldValue = "";

                if (!string.IsNullOrEmpty((string)oRow.Tag))
                {
                    switch ((string)oRow.Tag)
                    {
                        case "DistinguishedFolderId":
                            sFieldValue = "<t:DistinguishedFolderId Id=\"" + oRow.Cells[1].Value.ToString() + "\" />";
                            break;

                        case "FolderId":
                        case "FullFolderId":
                            sId = oRow.Cells[1].Value.ToString();
                            if (_distinguishedFolders.Contains(sId.ToLower()))
                            {
                                // This is a distinguished folder
                                sFieldValue = "<t:DistinguishedFolderId Id=\"" + sId.ToLower() + "\" />";
                            }
                            else
                            {
                                sFieldValue = String.Format("<t:FolderId Id=\"{0}\" />", sId);
                                if (((string)oRow.Tag).Equals("FullFolderId"))
                                    sFieldValue = "#SKIP#";
                            }
                            break;

                        case "ItemId":
                        case "FullItemId":
                            sId = oRow.Cells[1].Value.ToString();
                            sFieldValue = "<t:ItemId Id=\"" + sId + "\" />";
                            if (((string)oRow.Tag).Equals("FullItemId"))
                                sFieldValue = "#SKIP#";
                            break;

                        case "fullitemidChangeKey":
                            sFieldValue = String.Format("<t:ItemId Id=\"{0}\" ChangeKey=\"{1}\" />", sId, oRow.Cells[1].Value.ToString());
                            sFieldName = "ItemId";
                            break;

                        case "fullfolderidChangeKey":
                            sFieldValue = String.Format("<t:FolderId Id=\"{0}\" ChangeKey=\"{1}\" />", sId, oRow.Cells[1].Value.ToString());
                            sFieldName = "FolderId";
                            break;

                        case "AttachmentId":
                            sFieldValue = "<t:AttachmentId Id=\"" + oRow.Cells[1].Value.ToString() + "\" />";
                            break;

                        default:
                            // If we have a tag set, then this field is a multi-row field so we need to process all the rows
                            sFieldName = (string)oRow.Tag;
                            if (FieldType(sFieldName) == "getadditionalproperties")
                                sFieldValue += "<t:AdditionalProperties>";
                            string sFieldValuePart = "";
                            while ((string)oRow.Tag == sFieldName)
                            {
                                sFieldValuePart = "";
                                try
                                {
                                    sFieldValuePart = oRow.Cells[1].Value.ToString();
                                }
                                catch { }
                                i++;
                                // Check in case we have run out of rows!
                                if (i >= dataGridViewFields.Rows.Count)
                                    break;
                                sFieldValue += sFieldValuePart;
                                oRow = dataGridViewFields.Rows[i];
                            }
                            if (FieldType(sFieldName) == "getadditionalproperties")
                                sFieldValue += "</t:AdditionalProperties>";
                            i--;
                            break;
                    }
                }
                else
                {
                    switch (oRow.Cells[1].ValueType.ToString())
                    {
                        case "System.DateTime":
                            //sFieldValue = String.Format("{0:yyyy-MM-dd}T{0:HH:mm:ss}", oRow.Cells[1].Value);
                            if (!String.IsNullOrEmpty((string)oRow.Cells[1].Tag))
                                sFieldValue = String.Format("{0:" + (string)oRow.Cells[1].Tag + "}", oRow.Cells[1].Value);
                            else
                                sFieldValue = String.Format("{0:yyyy-MM-dd}T{0:HH:mm:ss}Z", oRow.Cells[1].Value);
                            break;
                        default:
                            sFieldValue = "";
                            try
                            {
                                sFieldValue = oRow.Cells[1].Value.ToString();
                            }
                            catch { }
                            break;
                    }
                }

                // Check for True/False, as EWS requires these in lower case
                if (sFieldValue.ToLower() == "true" || sFieldValue.ToLower() == "false")
                    sFieldValue = sFieldValue.ToLower();

                if ( String.IsNullOrEmpty(sFieldValue))
                {
                    // No value, so check if we have any delete markers (which identify the XML that needs to be removed if this field is blank)
                    if (_xml.Contains("<!--DELETEMARKERSTART:" + sFieldName + "-->"))
                    {
                        int iDeleteStart = _xml.IndexOf("<!--DELETEMARKERSTART:" + sFieldName + "-->");
                        string sDeleteEnd = "<!--DELETEMARKEREND:" + sFieldName + "-->";
                        int iDeleteEnd = _xml.IndexOf(sDeleteEnd);
                        if (iDeleteEnd >= 0)
                        {
                            iDeleteEnd += sDeleteEnd.Length;
                            _xml = _xml.Substring(0, iDeleteStart) + _xml.Substring(iDeleteEnd);
                        }
                    }
                }
                if (!sFieldValue.Equals("#SKIP#"))
                {
                    oRegEx = new Regex("<!--FIELD:" + sFieldName + ";.*?-->");
                    _xml = oRegEx.Replace(_xml, sFieldValue);
                }
            }
            oRegEx = new Regex("<!--DELETEMARKERSTART.*?-->");
            _xml = oRegEx.Replace(_xml, "");
            oRegEx = new Regex("<!--DELETEMARKEREND.*?-->");
            _xml = oRegEx.Replace(_xml, "");
        }

        private bool PopulateFieldList()
        {
            Regex oRegEx = new Regex("<!--FIELD:(.*?)-->", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            MatchCollection oMatches = oRegEx.Matches(_templateXML);
            dataGridViewFields.Rows.Clear();
            _fieldType = new Dictionary<string, string>();
            if (oMatches.Count == 0) return false;

            foreach (Match oMatch in oMatches)
            {
                AddField(oMatch.Value);
            }
            return true;
        }

        private void AddField(string FieldData)
        {
            // Add the field to the data grid
            
            // "<!--FIELD:Subject;field data-->"
            string sFieldName = FieldData.Substring(10);
            string sFieldValues = "";
            if (!sFieldName.Contains(";"))
                return;

            int iSemiColon = sFieldName.IndexOf(";");
            sFieldValues = sFieldName.Substring(iSemiColon + 1);
            if (sFieldValues.EndsWith("-->"))
                sFieldValues = sFieldValues.Substring(0, sFieldValues.Length - 3);
            sFieldName = sFieldName.Substring(0, iSemiColon);

            string[] sFieldSections = sFieldValues.Split(';');
            string sFieldType = sFieldSections[0];



            DataGridViewRow oRow=new DataGridViewRow();

            DataGridViewTextBoxCell oTextbox = new DataGridViewTextBoxCell();
            oTextbox.Value = sFieldName;
            oRow.Cells.Add(oTextbox);

            switch (sFieldType.ToLower())
            {
                case "string":
                case "itemid":
                case "fullitemid":
                case "attachmentid":
                case "folderid":
                case "fullfolderid":
                    
                    oTextbox = new DataGridViewTextBoxCell();
                    if (sFieldSections.GetUpperBound(0) == 1)
                    {
                        // We have a default value
                        oTextbox.Value = sFieldSections[1] ;
                    }
                    else oTextbox.Value="";
                    if (sFieldType.ToLower().EndsWith("itemid") && !String.IsNullOrEmpty(_itemId))
                        oTextbox.Value=_itemId;
                    if (sFieldType.ToLower().EndsWith("folderid") && !String.IsNullOrEmpty(_folderId))
                        oTextbox.Value=_folderId;
                    oRow.Cells.Add(oTextbox);
                    _fieldType.Add(sFieldName, sFieldType.ToLower());
                    if (sFieldType.ToLower() != "string")
                        oRow.Tag = sFieldType;
                    if (sFieldType.ToLower().Equals("fullitemid") || sFieldType.ToLower().Equals("fullfolderid"))
                    {
                        // We need ChangeKey added for this entry
                        dataGridViewFields.Rows.Add(oRow);
                        oRow = new DataGridViewRow();
                        oTextbox = new DataGridViewTextBoxCell();
                        oTextbox.Value = sFieldName + "ChangeKey";
                        oRow.Cells.Add(oTextbox);
                        oTextbox = new DataGridViewTextBoxCell();
                        oTextbox.Value = "";
                        oRow.Cells.Add(oTextbox);
                        oRow.Tag = String.Format("{0}ChangeKey", sFieldType.ToLower());
                        _fieldType.Add(sFieldName + "changekey", (string)oRow.Tag);
                    }
                    break;

                case "numeric":
                    oTextbox = new DataGridViewTextBoxCell();
                    if (sFieldSections.GetUpperBound(0) == 1)
                    {
                        // We have a default value
                        oTextbox.Value = sFieldSections[1] ;
                    }
                    else oTextbox.Value="";
                    oRow.Cells.Add(oTextbox);
                    _fieldType.Add(sFieldName, "numeric");
                    break;

                case "boolean":
                    DataGridViewCheckBoxCell oBooleanCell = new DataGridViewCheckBoxCell();
                    oBooleanCell.FalseValue = "false";
                    oBooleanCell.TrueValue = "true";
                    if (sFieldSections.GetUpperBound(0) == 1)
                    {
                        // We have a default value
                        if (sFieldSections[1].ToLower().Equals("true"))
                        {
                            oBooleanCell.Value = "true";
                        }
                    }
                    oBooleanCell.Value = "false";
                    oRow.Cells.Add(oBooleanCell);
                    _fieldType.Add(sFieldName, "boolean");
                    break;

                case "date":
                    //DataGridViewCalendarCell oDate = new DataGridViewCalendarCell();
                    DataGridViewDateTimeCell oDate = new DataGridViewDateTimeCell();
                    oDate.Value=DateTime.Now;
                    if (sFieldSections != null)
                    {
                        if (sFieldSections.GetUpperBound(0) > 0)
                        {
                            oDate.Style.Format = sFieldSections[1];
                            oDate.Tag = sFieldSections[1];
                        }
                    }
                    oRow.Cells.Add(oDate);
                    break;

                case "datetime":
                    DataGridViewDateTimeCell oDateCell = new DataGridViewDateTimeCell();
                    oDateCell.Value = DateTime.Now;
                    if (sFieldSections != null)
                    {
                        if (sFieldSections.GetUpperBound(0) > 0)
                            oDateCell.Tag = sFieldSections[1];
                    }
                    oRow.Cells.Add(oDateCell);
                    break;

                case "listenerurl":
                    oTextbox = new DataGridViewTextBoxCell();
                    oTextbox.Value = GetListenerUrl();
                    oRow.Cells.Add(oTextbox);
                    break;

                // <!--FIELD:Event Type;ElementCheckedList;t:EventType;CreatedEvent,DeletedEvent,ModifiedEvent,CopiedEvent,MovedEvent-->
                case "elementcheckedlist":
                    string[] sListItems = sFieldSections[2].Split(',');
                    for (int i = 0; i <= sListItems.GetUpperBound(0); i++)
                    {
                        oRow = new DataGridViewRow();
                        oRow.Tag = sFieldName;
                        oTextbox = new DataGridViewTextBoxCell();
                        oTextbox.Value = sFieldName + ": " + sListItems[i];
                        oRow.Cells.Add(oTextbox);
                        DataGridViewCheckBoxCell oCheckCell = new DataGridViewCheckBoxCell();
                        oCheckCell.TrueValue = "<" + sFieldSections[1] + ">" + sListItems[i] + "</" + sFieldSections[1] + ">";
                        oCheckCell.Value = false;
                        oRow.Cells.Add(oCheckCell);
                        dataGridViewFields.Rows.Add(oRow);
                    }
                    return;

                // <!--FIELD:Additional Properties;GetAdditionalProperties;Name,PropertySetId,PropertyId,PropertyType|Name,PropertySetId,PropertyId,PropertyType-->
                case "getadditionalproperties":
                    _fieldType.Add(sFieldName, "getadditionalproperties");
                    string[] sProperties = sFieldSections[1].Split('|');
                    for (int i = 0; i <= sProperties.GetUpperBound(0); i++)
                    {
                        string[] sProperty = sProperties[i].Split(',');
                        oRow = new DataGridViewRow();
                        oRow.Tag = sFieldName;
                        oTextbox = new DataGridViewTextBoxCell();
                        oTextbox.Value = sProperty[0];
                        oRow.Cells.Add(oTextbox);
                        DataGridViewCheckBoxCell oCheckCell = new DataGridViewCheckBoxCell();
                        if (sProperty.GetUpperBound(0) == 3)
                        {
                            string[] sDistinguishedPropertySetId = { "address", "appointment", "calendarAssistant", "common", "internetheaders", "meeting", "publicstrings", "task", "unifiedmessaging" };
                            if (sDistinguishedPropertySetId.Contains(sProperty[1].ToLower()))
                            {
                                // This is an extended property from a known distinguished property set
                                oCheckCell.TrueValue = String.Format("<t:ExtendedFieldURI DistinguishedPropertySetId=\"{0}\" PropertyId=\"{1}\" PropertyType=\"{2}\" />",
                                    sProperty[1], sProperty[2], sProperty[3]);
                            }
                            else
                            {
                                oCheckCell.TrueValue = String.Format("<t:ExtendedFieldURI PropertySetId=\"{0}\" PropertyId=\"{1}\" PropertyType=\"{2}\" />",
                                    sProperty[1], sProperty[2], sProperty[3]);
                            }
                        }
                        else if (sProperty.GetUpperBound(0) == 2)
                        {
                            oCheckCell.TrueValue = String.Format("<t:ExtendedFieldURI PropertyTag=\"{0}\" PropertyType=\"{1}\" />",
                                sProperty[1], sProperty[2]);
                        }
                        oRow.Cells.Add(oCheckCell);
                        dataGridViewFields.Rows.Add(oRow);
                    }
                    return;

                case "distinguishedfolderid":
                    // This is a DistinguishedFolderId
                    using (DataGridViewComboBoxCell oComboBox = new DataGridViewComboBoxCell())
                    {
                        foreach (string sValue in _distinguishedFolders)
                            oComboBox.Items.Add(sValue);
                        oComboBox.Value = _distinguishedFolders[0];
                        oRow.Cells.Add(oComboBox);
                    }
                    oRow.Tag = "DistinguishedFolderId";
                    break;


                case "list":
                    // This is a list of values
                    string[] sValues = sFieldSections[1].Split(',');

                    using (DataGridViewComboBoxCell oComboBox = new DataGridViewComboBoxCell())
                    {
                        foreach (string sValue in sValues)
                            oComboBox.Items.Add(sValue);
                        oComboBox.Value = sValues[0];
                        try
                        {
                            if (!String.IsNullOrEmpty(sFieldSections[2]))
                            {
                                oComboBox.Value = sFieldSections[2];
                            }
                        }
                        catch {}
                        oRow.Cells.Add(oComboBox);
                    }
                    break;

                default:
                    System.Windows.Forms.MessageBox.Show("The field \"" + sFieldType + "\" is not recognised,",
                        "Invalid field", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
            dataGridViewFields.Rows.Add(oRow);
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private string GetListenerUrl()
        {
            try
            {
                return (_owner as FormMain).HTTPListener.URi;
            }
            catch
            {
                return "";
            }
        }

        private void dataGridViewFields_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            System.Windows.Forms.MessageBox.Show(String.Format("Unexpected datagrid error: {0}{1}", Environment.NewLine, e.Exception.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void dataGridViewFields_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            UpdateXml();
        }

        private void UpdateXml()
        {
            // Update the XML with the field values, and show

            ReplaceFields();
            xmlEditor1.Text = _xml;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            _cancel = true;
            this.Hide();
        }

        private void comboBoxTemplateFolder_SelectedIndexChanged(object sender, EventArgs e)
        {
            ReadAvailableTemplates();
        }

        private void comboBoxTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxTemplate.Text.Equals(_currentTemplateName) || (String.IsNullOrEmpty(comboBoxTemplate.Text)))
            {
                buttonLoad.Enabled = false;
                return;
            }

            buttonLoad.Enabled = true;
            if (String.IsNullOrEmpty(xmlEditor1.Text))
                buttonLoad_Click(null, null);
        }

        private void buttonLoad_Click(object sender, EventArgs e)
        {
            string sTemplate = ReadTemplate();
            if (String.IsNullOrEmpty(sTemplate))
                return;
            _templateXML = sTemplate;
            
            PopulateFieldList();
            xmlEditor1.Text = _templateXML;
            UpdateXml();
            buttonLoad.Enabled = false;
        }

    }


}
