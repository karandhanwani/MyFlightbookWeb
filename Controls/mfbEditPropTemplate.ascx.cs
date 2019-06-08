﻿using MyFlightbook;
using MyFlightbook.Templates;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.UI;
using System.Web.UI.WebControls;

/******************************************************
 * 
 * Copyright (c) 2019 MyFlightbook LLC
 * Contact myflightbook-at-gmail.com for more information
 *
*******************************************************/

public partial class Controls_mfbEditPropTemplate : System.Web.UI.UserControl
{
    public event EventHandler<PropertyTemplateEventArgs> TemplateCreated = null;

    #region Properties
    private const string szvsActiveTemplate = "vsActiveTemplate";
    public UserPropertyTemplate ActiveTemplate
    {
        get { return (UserPropertyTemplate)ViewState[szvsActiveTemplate]; }
        set { ViewState[szvsActiveTemplate] = value; }
    }
    #endregion

    protected void Page_Load(object sender, EventArgs e)
    {
        Page.ClientScript.RegisterClientScriptInclude("ListDrag", ResolveClientUrl("~/Public/Scripts/listdrag.js?v=4"));
        Page.ClientScript.RegisterClientScriptInclude("filterDropdown", ResolveClientUrl("~/Public/Scripts/DropDownFilter.js?v=3"));
        searchProps.TextBoxControl.Attributes["onkeyup"] = String.Format(CultureInfo.InvariantCulture, "FilterProps(this, '{0}', '{1}', '{2}')", divAvailableProps.ClientID, lblFilteredLabel.ClientID, Resources.LogbookEntry.PropertiesFound);

        if (!IsPostBack)
        {
            if (ActiveTemplate == null)
                ActiveTemplate = new UserPropertyTemplate() { Owner = Page.User.Identity.Name, OriginalOwner = Page.User.Identity.Name };

            PropertyTemplateGroup[] rgGroups = (PropertyTemplateGroup[]) Enum.GetValues(typeof(PropertyTemplateGroup));

            foreach (PropertyTemplateGroup ptg in rgGroups)
                if (ptg != PropertyTemplateGroup.Automatic)
                    cmbCategories.Items.Add(new ListItem(PropertyTemplate.NameForGroup(ptg), ptg.ToString()));
            cmbCategories.SelectedIndex = 0;

            ToForm();

            locTemplateDescription1.Text = Branding.ReBrand(Resources.LogbookEntry.TemplateDescription);
            locTemplateDescription2.Text = Branding.ReBrand(Resources.LogbookEntry.TemplateDescription2);
        }
    }

    protected void UpdateLists()
    {
        UserPropertyTemplate pt = ActiveTemplate;
        List<CustomPropertyType> lstAll = new List<CustomPropertyType>(CustomPropertyType.GetCustomPropertyTypes());
        rptTemplateProps.DataSource = lstAll.FindAll(cpt => pt.ContainsProperty(cpt));
        rptTemplateProps.DataBind();
        lstAll.RemoveAll(cpt => pt.ContainsProperty(cpt));
        rptAvailableProps.DataSource = lstAll;
        rptAvailableProps.DataBind();
    }

    protected void ToForm()
    {
        searchProps.SearchText = string.Empty;  // clear the filter before updating the lists.
        UpdateLists();

        UserPropertyTemplate pt = ActiveTemplate;
        txtTemplateName.Text = pt.Name;
        txtDescription.Text = pt.Description;
        cmbCategories.SelectedValue = pt.Group.ToString();

        List<TemplateCollection> rgtc = new List<TemplateCollection>(TemplateCollection.GroupTemplates(UserPropertyTemplate.TemplatesForUser(Page.User.Identity.Name)));
        rgtc.RemoveAll(tc => tc.Group == PropertyTemplateGroup.Automatic);
        gvPropertyTemplates.DataSource = rgtc;
        gvPropertyTemplates.DataBind();
    }

    protected void btnAddToTemplate_Click(object sender, EventArgs e)
    {
        if (!String.IsNullOrEmpty(txtPropID.Text) && !String.IsNullOrEmpty(txtPropID.Text.Trim()))
        {
            try
            {
                int idPropType = Convert.ToInt32(txtPropID.Text, CultureInfo.InvariantCulture);
                ActiveTemplate.AddProperty(idPropType);
                UpdateLists();
            }
            catch
            {
                throw new MyFlightbookValidationException(String.Format(CultureInfo.CurrentCulture, "Error Parsing proptype '{0}' for edit template (add) in invariant culture.  Current culture is {1}.", txtPropID.Text, CultureInfo.CurrentCulture.DisplayName));
            }
        }
    }

    protected void btnRemoveFromTemplate_Click(object sender, EventArgs e)
    {
        if (!String.IsNullOrEmpty(txtPropID.Text) && !String.IsNullOrEmpty(txtPropID.Text.Trim()))
        {
            try
            {
                int idPropType = Convert.ToInt32(txtPropID.Text, CultureInfo.InvariantCulture);
                ActiveTemplate.RemoveProperty(idPropType);
                UpdateLists();
            }
            catch
            {
                throw new MyFlightbookValidationException(String.Format(CultureInfo.CurrentCulture, "Error Parsing proptype '{0}' for edit template (remove) in invariant culture.  Current culture is {1}.", txtPropID.Text, CultureInfo.CurrentCulture.DisplayName));
            }
        }
    }

    protected void btnSaveTemplate_Click(object sender, EventArgs e)
    {
        Page.Validate("vgPropTemplate");
        if (!Page.IsValid)
            return;

        ActiveTemplate.Name = txtTemplateName.Text;
        ActiveTemplate.Description = txtDescription.Text;
        ActiveTemplate.Group = (PropertyTemplateGroup) Enum.Parse(typeof(PropertyTemplateGroup), cmbCategories.SelectedValue);
        ActiveTemplate.Owner = Page.User.Identity.Name;
        try
        {
            ActiveTemplate.Commit();
            if (TemplateCreated != null)
                TemplateCreated(this, new PropertyTemplateEventArgs(ActiveTemplate));

            // Clear for the next
            ActiveTemplate = new UserPropertyTemplate();
            ToForm();
            cpeNewTemplate.ClientState = "true";
        }
        catch (MyFlightbookValidationException ex)
        {
            lblErr.Text = ex.Message;
        }
    }

    protected void gvTemplates_RowCommand(object sender, GridViewCommandEventArgs e)
    {
        if (e == null)
            throw new ArgumentNullException("e");

        UserPropertyTemplate pt = new UserPropertyTemplate(Convert.ToInt32(e.CommandArgument, CultureInfo.InvariantCulture));

        if (e.CommandName.CompareCurrentCultureIgnoreCase("_Delete") == 0)
        {
            pt.Delete();
            ToForm();
        }
        else if (e.CommandName.CompareCurrentCultureIgnoreCase("_edit") == 0)
        {
            ActiveTemplate = pt;
            ToForm();
            cpeNewTemplate.Collapsed = false;
            cpeNewTemplate.ClientState = "false";
        }
    }

    protected void ckIsPublic_CheckedChanged(object sender, EventArgs e)
    {
        if (sender == null)
            throw new ArgumentNullException("sender");

        CheckBox ck = (CheckBox)sender;
        GridViewRow gvr = ck.NamingContainer as GridViewRow;
        HiddenField h = (HiddenField)gvr.FindControl("hdnID");
        UserPropertyTemplate pt = new UserPropertyTemplate(Convert.ToInt32(h.Value, CultureInfo.InvariantCulture));

        if (ck.Checked)
        {
            List<PropertyTemplate> lst = new List<PropertyTemplate>(UserPropertyTemplate.PublicTemplates());
            if (lst.Find(ptPublic => ptPublic.Name.CompareCurrentCultureIgnoreCase(pt.Name) == 0 && ptPublic.Owner.CompareCurrentCultureIgnoreCase(pt.Owner) != 0) != null)
            {
                ((Label) gvr.FindControl("lblPublicErr")).Text = String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.TemplateDuplicateSharedName, pt.Name);
                ck.Checked = false;
                return;
            }
        }

        pt.IsPublic = ck.Checked;
        pt.Commit();
    }

    protected bool MatchesFilter(string text)
    {
        if (String.IsNullOrEmpty(text))
            return true;

        text = text.ToUpper(CultureInfo.CurrentCulture);
        string[] words = Regex.Split(searchProps.SearchText.ToUpper(CultureInfo.CurrentCulture), "\\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (string word in words)
        {
            if (String.IsNullOrWhiteSpace(word))
                continue;
            if (!text.Contains(word))
                return false;
        }

        return true;
    }

    protected string StyleForTitle(string text)
    {
        return MatchesFilter(text) ? string.Empty : "display: none;";
    }

    protected void searchProps_SearchClicked(object sender, EventArgs e)
    {
        UpdateLists();
    }
}