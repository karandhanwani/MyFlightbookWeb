using MyFlightbook;
using MyFlightbook.ImportFlights;
using MyFlightbook.OAuth.CloudAhoy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.UI;
using System.Web.UI.WebControls;

/******************************************************
 * 
 * Copyright (c) 2007-2020 MyFlightbook LLC
 * Contact myflightbook-at-gmail.com for more information
 *
*******************************************************/

public partial class Member_Import : MyFlightbook.Web.WizardPage.MFBWizardPage
{
    protected bool UseHHMM { get; set;}
    private const string szKeyVSCSVImporter = "viewStateKeyCSVImporter";
    private const string szKeyVSCSVData = "viewStateCSVData";
    private const string szKeyVSPendingOnly = "viewstatePendingOnly";

    protected bool IsPendingOnly
    {
        get { return ViewState[szKeyVSPendingOnly] != null; }
        set { ViewState[szKeyVSPendingOnly] = (value ? value.ToString(CultureInfo.InvariantCulture) : null); }
    }

    /// <summary>
    /// The CSVImporter that is in progress
    /// </summary>
    protected CSVImporter CurrentImporter
    {
        get { return (CSVImporter)ViewState[szKeyVSCSVImporter]; }
        set
        {
            if (value == null)
                ViewState.Remove(szKeyVSCSVImporter);
            else
                ViewState[szKeyVSCSVImporter] = value;
        }
    }

    /// <summary>
    /// The uploaded data
    /// </summary>
    private byte[] CurrentCSVSource
    {
        get { return (byte[])ViewState[szKeyVSCSVData]; }
        set
        {
            if (value == null)
                ViewState.Remove(szKeyVSCSVData);
            else
                ViewState[szKeyVSCSVData] = value;
        }
    }

    private readonly Dictionary<int, string> m_errorContext = new Dictionary<int,string>();
    private Dictionary<int, string> ErrorContext
    {
        get { return m_errorContext; }
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Master.SelectedTab = tabID.lbtImport;
        InitWizard(wzImportFlights);

        Title = (string)GetLocalResourceObject("PageResource1.Title");
        Profile pf = MyFlightbook.Profile.GetUser(User.Identity.Name);
        UseHHMM = pf.UsesHHMM;

        if (!IsPostBack)
        {
            pnlCloudAhoy.Visible = pf.CloudAhoyToken != null && pf.CloudAhoyToken.AccessToken != null;
            List<FAQItem> lst = new List<FAQItem>(FAQItem.CachedFAQItems);
            FAQItem fi = lst.Find(f => f.idFAQ == 44);
            if (fi != null)
            {
                lblTipsHeader.Text = fi.Question;
                litTipsFAQ.Text = fi.Answer;
            }
        }
    }

    protected static void AddTextRow(Control parent, string sz, string szClass = "")
    {
        if (parent == null)
            throw new ArgumentNullException(nameof(parent));
        Panel p = new Panel();
        parent.Controls.Add(p);
        Label l = new Label();
        p.Controls.Add(l);
        if (!String.IsNullOrEmpty(szClass))
            p.CssClass = szClass;
        l.Text = sz;
    }

    protected void rptPreview_ItemDataBound(object sender, RepeaterItemEventArgs e)
    {
        if (e == null)
            throw new ArgumentNullException(nameof(e));

        LogbookEntryBase le = (LogbookEntryBase)e.Item.DataItem;

        PlaceHolder plc = (PlaceHolder)e.Item.FindControl("plcAdditional");
        // throw the less used properties into the final column
        if (le.EngineStart.HasValue())
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Engine Start: {0}", le.EngineStart.UTCDateFormatString()));
        if (le.EngineEnd.HasValue())
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Engine End: {0}", le.EngineEnd.UTCDateFormatString()));
        if (le.FlightStart.HasValue())
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Flight Start: {0}", le.FlightStart.UTCDateFormatString()));
        if (le.FlightEnd.HasValue())
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Flight End: {0}", le.FlightEnd.UTCDateFormatString()));
        if (le.HobbsStart != 0)
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Hobbs Start: {0}", le.HobbsStart));
        if (le.HobbsEnd != 0)
            AddTextRow(plc, String.Format(CultureInfo.CurrentCulture, "Hobbs End: {0}", le.HobbsEnd));

        // Add a concatenation of each property to the row as well.
        foreach (CustomFlightProperty cfp in le.CustomProperties)
            AddTextRow(plc, UseHHMM ? cfp.DisplayStringHHMM : cfp.DisplayString);

        if (!String.IsNullOrEmpty(le.ErrorString))
        {
            int iRow = e.Item.ItemIndex + 1;

            if (ErrorContext.ContainsKey(iRow))
            {
                ((Label)e.Item.FindControl("lblRawRow")).Text = ErrorContext[iRow];
                e.Item.FindControl("rowError").Visible = true;
                ((System.Web.UI.HtmlControls.HtmlTableRow)e.Item.FindControl("rowFlight")).Attributes["class"] = "error";
            }
            else
                e.Item.FindControl("imgNewOrUpdate").Visible = false;
        }

        if (!le.IsNewFlight && CurrentImporter != null && CurrentImporter.OriginalFlightsToModify.ContainsKey(le.FlightID))
        {
            List<PropertyDelta> lst = new List<PropertyDelta>(CurrentImporter.OriginalFlightsToModify[le.FlightID].CompareTo(le, UseHHMM));
            if (lst.Count > 0)
            {
                e.Item.FindControl("pnlDiffs").Visible = true;
                Repeater diffs = (Repeater)e.Item.FindControl("rptDiffs");
                diffs.DataSource = lst;
                diffs.DataBind();
            }
        }
    }

    protected static void AddSuccessRow(LogbookEntryBase le, int iRow) { }

    protected void AddErrorRow(LogbookEntryBase le, string szContext, int iRow)
    {
        if (le == null)
            throw new ArgumentNullException(nameof(le));

        if (IsPendingOnly && le.LastError != LogbookEntryBase.ErrorCode.None)   // ignore errors if the importer is only pending flights and the error is a logbook validation error (no tail, future date, night, etc.)
            return;

        // if we're here, we are *either* not pending only *or* we didn't have a logbookentry validation error (e.g., could be malformed row)
        ErrorContext[iRow] = szContext; // save the context for data bind
        AddTextRow(plcErrorList, String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.errImportRowHasError, iRow, le.ErrorString), "error");
    }

    protected void UploadData()
    {
        if (!fuPreview.HasFile && CurrentCSVSource != null && CurrentCSVSource.Length > 0)
        {
            // re-parse it.
            PreviewData();
            return;
        }

        plcErrorList.Controls.Clear();

        ViewState[szKeyVSCSVImporter] = null;

        if (fuPreview.FileBytes.Length > 0)
        {
            CurrentCSVSource = fuPreview.FileBytes;
            PreviewData();
        }
        else
        {
            lblFileRequired.Text = Resources.LogbookEntry.errImportInvalidCSVFile;
            SetWizardStep(wsUpload);
        }
    }

    protected void PreviewData()
    {
        lblError.Text = string.Empty;

        mvPreviewResults.SetActiveView(vwPreviewResults);   // default to showing results.

        mfbImportAircraft1.CandidatesForImport = Array.Empty<AircraftImportMatchRow>(); // start fresh every time.

        byte[] rgb = CurrentCSVSource;
        if (rgb == null || rgb.Length == 0)
        {
            lblFileRequired.Text = Resources.LogbookEntry.errImportInvalidCSVFile;
            SetWizardStep(wsUpload);
            return;
        }

        pnlConverted.Visible = pnlAudit.Visible = false;

        ExternalFormatConvertResults results = ExternalFormatConvertResults.ConvertToCSV(rgb);
        lblAudit.Text = results.AuditResult;
        hdnAuditState.Value = results.ResultString;
        CurrentCSVSource = results.GetConvertedBytes();
        IsPendingOnly = results.IsPendingOnly;

        if (!String.IsNullOrEmpty(results.ConvertedName))
        {
            lblFileWasConverted.Text = String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.importLabelFileWasConverted, results.ConvertedName);
            pnlConverted.Visible = true;
        }

        pnlAudit.Visible = (results.IsFixedOrBroken);
        if (results.IsBroken)
        {
            lblAudit.CssClass = "error";
            ExpandoAudit.ExpandoControl.Collapsed = false;
            ExpandoAudit.ExpandoControl.ClientState = "false";
        }
        else
        {
            lblAudit.CssClass = string.Empty;
            ExpandoAudit.ExpandoControl.Collapsed = true;
            ExpandoAudit.ExpandoControl.ClientState = "true";
        }

        pnlAudit.Visible = pnlConverted.Visible || !String.IsNullOrEmpty(lblAudit.Text);

        ErrorContext.Clear();
        CSVImporter csvimporter = CurrentImporter = new CSVImporter(mfbImportAircraft1.ModelMapping);
        csvimporter.InitWithBytes(rgb, User.Identity.Name, AddSuccessRow, AddErrorRow, ckAutofill.Checked);

        if (csvimporter.FlightsToImport == null)
        {
            lblFileRequired.Text = csvimporter.ErrorMessage;
            SetWizardStep(wsUpload);
            return;
        }

        rptPreview.DataSource = csvimporter.FlightsToImport;
        rptPreview.DataBind();
        mvPreview.SetActiveView(csvimporter.FlightsToImport.Count > 0 ? vwPreview : vwNoResults);

        mvMissingAircraft.SetActiveView(vwNoMissingAircraft); // default state.

        if (csvimporter.FlightsToImport.Count > 0)
        {
            if (csvimporter.HasErrors)
            {
                if (!IsPendingOnly)
                    lblError.Text = Resources.LogbookEntry.ImportPreviewNotSuccessful;

                List<AircraftImportMatchRow> missing = new List<AircraftImportMatchRow>(csvimporter.MissingAircraft);
                if (missing.Count > 0)
                {
                    mfbImportAircraft1.CandidatesForImport = missing;
                    mvMissingAircraft.SetActiveView(vwAddMissingAircraft);
                }

                ((Button)wzImportFlights.FindControl("FinishNavigationTemplateContainerID$btnNewFile")).Visible = true;
            }

            ((AjaxControlToolkit.ConfirmButtonExtender)wzImportFlights.FindControl("FinishNavigationTemplateContainerID$confirmImportWithErrors")).Enabled = csvimporter.HasErrors;
        }
    }

    protected void SetWizardStep(WizardStep ws)
    {
        for (int i = 0; i < wzImportFlights.WizardSteps.Count; i++)
            if (wzImportFlights.WizardSteps[i] == ws)
            {
                wzImportFlights.ActiveStepIndex = i;
                break;
            }
    }

    protected void Import(object sender, WizardNavigationEventArgs e)
    {
        CSVImporter csvimporter = CurrentImporter;

        if (e == null)
            throw new ArgumentNullException(nameof(e));

        if (csvimporter == null)
        {
            lblError.Text = Resources.LogbookEntry.ImportNotSuccessful;
            e.Cancel = true;
            SetWizardStep(wsPreview);
            PreviewData();  // rebuild the table.
            return;
        }

        int cFlightsAdded = 0;
        int cFlightsUpdated = 0;
        int cFlightsWithErrors = 0;

        csvimporter.FCommit((le, fIsNew) =>
                {
                    if (String.IsNullOrEmpty(le.ErrorString))
                    {
                        AddTextRow(plcProgress, String.Format(CultureInfo.CurrentCulture, fIsNew ? Resources.LogbookEntry.ImportRowAdded : Resources.LogbookEntry.ImportRowUpdated, le.ToString()), "success");
                        if (fIsNew)
                            cFlightsAdded++;
                        else
                            cFlightsUpdated++;
                    }
                    else
                    {
                        PendingFlight pf = new PendingFlight(le) { User = User.Identity.Name };
                        pf.Commit();
                        lnkPending.Visible = true;
                        AddTextRow(plcProgress, String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.ImportRowAddedPending, le.ToString(), le.ErrorString), "error");
                        cFlightsWithErrors++;
                    }
                }, 
                (le, ex) =>
                {
                    AddTextRow(plcProgress, String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.ImportRowNotAdded, le.ToString(), ex.Message), "error");
                }, 
                true);

        List<string> lstResults = new List<string>();
        if (cFlightsAdded > 0)
            lstResults.Add(String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.ImportFlightsAdded, cFlightsAdded));
        if (cFlightsUpdated > 0)
            lstResults.Add(String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.ImportFlightsUpdated, cFlightsUpdated));
        if (cFlightsWithErrors > 0)
            lstResults.Add(String.Format(CultureInfo.CurrentCulture, Resources.LogbookEntry.ImportFlightsWithErrors, cFlightsWithErrors));
        rptImportResults.DataSource = lstResults;
        rptImportResults.DataBind();
        Profile.GetUser(Page.User.Identity.Name).SetAchievementStatus();
        mvPreviewResults.SetActiveView(vwImportResults);
        wzImportFlights.Visible = false;
        pnlImportSuccessful.Visible = true;
        CurrentImporter = null;
        CurrentCSVSource = null;
    }

    protected void wzImportFlights_ActiveStepChanged(object sender, EventArgs e)
    {
        if (wzImportFlights.ActiveStep == wsMissingAircraft)
            UploadData();
        else if (wzImportFlights.ActiveStep == wsPreview)
            PreviewData();
        else
        {
            CurrentCSVSource = null;
            CurrentImporter = null;
        }

        mvContent.ActiveViewIndex = wzImportFlights.ActiveStepIndex;    // keep bottom half and top half in sync
    }

    protected void lnkDefaultTemplate_Click(object sender, EventArgs e)
    {
        const string szHeaders = @"Date,Tail Number,Approaches,Hold,Landings,FS Night Landings,FS Day Landings,X-Country,Night,IMC,Simulated Instrument,Ground Simulator,Dual Received,CFI,SIC,PIC,Total Flight Time,Route,Comments";

        Response.DownloadToFile(szHeaders.Replace(",", CultureInfo.CurrentCulture.TextInfo.ListSeparator), "text/csv", Branding.CurrentBrand.AppName, "csv");
    }

    protected void btnDownloadConverted_Click(object sender, EventArgs e)
    {
        // Give it a name that is the brand name, user's name, and date.  Convert spaces to dashes, and then strip out ANYTHING that is not alphanumeric or a dash.
        Response.DownloadToFile(CurrentCSVSource, 
            "text/csv",
            String.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}{3}", Branding.CurrentBrand.AppName, Profile.GetUser(Page.User.Identity.Name).UserFullName, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), hdnAuditState.Value).Replace(" ", "-"),
            "csv");
    }

    protected void btnNewFile_Click(object sender, EventArgs e)
    {
        if (sender == null)
            throw new ArgumentNullException(nameof(sender));
        SetWizardStep(wsUpload);
        ((Control)sender).Visible = false;
    }

    protected async void btnImportCloudAhoy_Click(object sender, EventArgs e)
    {
        DateTime? dtStart = null;
        if (mfbCloudAhoyStartDate.Date.HasValue())
            dtStart = mfbCloudAhoyStartDate.Date;
        DateTime? dtEnd = null;
        if (mfbCloudAhoyEndDate.Date.HasValue())
            dtEnd =  mfbCloudAhoyEndDate.Date;

        string szResult = await CloudAhoyClient.ImportCloudAhoyFlights(Page.User.Identity.Name, !Branding.CurrentBrand.MatchesHost(Request.Url.Host), dtStart, dtEnd).ConfigureAwait(true);
        if (String.IsNullOrEmpty(szResult))
        {
            // Avoid a "Thread was being aborted" (ThreadAbortException).
            Response.Redirect("~/Member/ReviewPendingFlights.aspx", false);
            Context.ApplicationInstance.CompleteRequest();
        }
        else
        {
            lblCloudAhoyErr.Text = szResult;
            popupCloudAhoy.Show();
        }
    }
}
