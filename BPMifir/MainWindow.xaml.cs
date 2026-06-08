using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using Microsoft.Win32;
using System.Xml.Serialization;
using System.Windows.Resources;
using System.Globalization;



namespace BPMifir
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        // Windows.Storage.ApplicationDataContainer localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

        public MainWindow()
        {      
            //LEI.Text = localSettings.Values["DestinationCountryCode"]!=null ? localSettings.Values["DestinationCountryCode"].ToString() : "XXXXXXXXXXXXXXXXXXXX";
            InitializeComponent();

        }

        private void LoadTransactionsBtn_Click(object sender, RoutedEventArgs e)
        {
            /*
            localSettings.Values["LEI"] = LEI.Text;
            localSettings.Values["DestinationCountryCode"] = DestinationCountryCode.Text;
            localSettings.Values["SequenceNumber"] = SequenceNumber.Text;
            localSettings.Values["PreviousSequenceNumber"] = PreviousSequenceNumber.Text;
            localSettings.Values["Version"] = Version.Text;
            */

            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".xml"; // Required file extension 
            fileDialog.Filter = "Xml documents (.xml)|*.xml";

            Nullable<bool> result = fileDialog.ShowDialog();
            if (result == true)
            {
                var x = fileDialog.FileName;
                XmlSerializer transactionsSerializer = new XmlSerializer(typeof(DB.reportFile));
                DB.reportFile dbDoc;


                //Read Transactions XML
                try
                {
                    using (var fileStream = File.Open(fileDialog.FileName, FileMode.Open))
                    {
                        dbDoc = (DB.reportFile)transactionsSerializer.Deserialize(fileStream);
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show("Error processing the trasactions report file."+"\n"+ex?.Message, "Processing Error");
                    return;
                }

                try 
                {
                    List<string> missingReportFields = GetMissingFields(
                        ("Reporting entity code", ReportingEntityCode.Text),
                        ("Destination country code", DestinationCountryCode.Text),
                        ("Report sequence number", SequenceNumber.Text),
                        ("Version", Version.Text),
                        ("Previous sequence number", PreviousSequenceNumber.Text),
                        ("Transaction record container", dbDoc.record == null ? null : "present"),
                        ("Transaction records", dbDoc.record?.Items?.Length > 0 ? "present" : null))
                        .ToList();

                    if (missingReportFields.Count > 0)
                    {
                        MessageBox.Show(
                            "The ESMA report cannot be generated because mandatory report-level fields are missing:\n\n"
                            + string.Join("\n", missingReportFields),
                            "Missing Mandatory Fields");
                        return;
                    }

                    //Prepare CySec Document
                    BizData cyDoc = new BizData();

                    //CySec Header
                    cyDoc.Hdr = new BizDataHdr();
                    cyDoc.Hdr.AppHdr = new AppHdr();
                    cyDoc.Hdr.AppHdr.Fr = new AppHdrFR();
                    cyDoc.Hdr.AppHdr.Fr.OrgId = new AppHdrFROrgId();
                    cyDoc.Hdr.AppHdr.Fr.OrgId.Id = new AppHdrFROrgIdID();
                    cyDoc.Hdr.AppHdr.Fr.OrgId.Id.OrgId = new AppHdrFROrgIdIDOrgId();
                    cyDoc.Hdr.AppHdr.Fr.OrgId.Id.OrgId.Othr = new AppHdrFROrgIdIDOrgIdOthr();
                    cyDoc.Hdr.AppHdr.Fr.OrgId.Id.OrgId.Othr.Id = ReportingEntityCode.Text;

                    cyDoc.Hdr.AppHdr.To = new AppHdrTO();
                    cyDoc.Hdr.AppHdr.To.OrgId = new AppHdrTOOrgId();
                    cyDoc.Hdr.AppHdr.To.OrgId.Id = new AppHdrTOOrgIdID();
                    cyDoc.Hdr.AppHdr.To.OrgId.Id.OrgId = new AppHdrTOOrgIdIDOrgId();
                    cyDoc.Hdr.AppHdr.To.OrgId.Id.OrgId.Othr = new AppHdrTOOrgIdIDOrgIdOthr();
                    cyDoc.Hdr.AppHdr.To.OrgId.Id.OrgId.Othr.Id = DestinationCountryCode.Text;

                    string BizMsgIdr = ReportingEntityCode.Text + "_DATTRA_"
                        + DestinationCountryCode.Text + "_"+SequenceNumber.Text + "-"
                        + Version.Text + "-" + PreviousSequenceNumber.Text
                        + "_" + DateTime.Now.ToString("yy");

                    cyDoc.Hdr.AppHdr.BizMsgIdr = BizMsgIdr;
                    cyDoc.Hdr.AppHdr.MsgDefIdr = "auth.016.001.01";
                    cyDoc.Hdr.AppHdr.CreDt = DateTime.Now.ToUniversalTime().ToString("s")+ "Z";

                    //Payload
                    cyDoc.Pyld = new BizDataPyld();
                    cyDoc.Pyld.Document = new Document();
                
                    //Add FinInstrmRptgTxRpt
                    cyDoc.Pyld.Document.FinInstrmRptgTxRpt = new ReportingTransactionType1Choice__1[dbDoc.record.Items.Count()];
                    List<string> ignoredTransactions = new List<string>();
                    
                    //Add Transactions
                    for (int i = 0; i< dbDoc.record.Items.Count(); i++)
                    {
                        cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i] = new ReportingTransactionType1Choice__1();
                        DB.transactionPositionReport dbtr = (DB.transactionPositionReport)dbDoc.record.Items[i];

                        if (dbtr.processingDetails.actionType == DB.processingDetailsActionType.C)
                        {
                            string cancellationExecutingParty =
                                dbtr.mifir?.counterpartyDetails?.executingEntityId?.LEI;
                            string cancellationSubmittingParty = dbDoc.fileInformation?.sender;

                            if (string.IsNullOrWhiteSpace(dbtr.processingDetails.customerTransactionId)
                                || string.IsNullOrWhiteSpace(cancellationExecutingParty)
                                || string.IsNullOrWhiteSpace(cancellationSubmittingParty))
                            {
                                ignoredTransactions.Add(
                                    GetIgnoredTransactionMessage(
                                        "Cancellation",
                                        dbtr.processingDetails.customerTransactionId,
                                        GetMissingFields(
                                            ("Transaction reference number", dbtr.processingDetails.customerTransactionId),
                                            ("Executing entity identification code", cancellationExecutingParty),
                                            ("Submitting entity identification code", cancellationSubmittingParty))));

                                continue;
                            }

                            cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item = new SecuritiesTransactionReport2__1()
                            {
                                TxId = dbtr.processingDetails.customerTransactionId,
                                ExctgPty = cancellationExecutingParty,
                                SubmitgPty = cancellationSubmittingParty
                            };

                            continue;
                        }

                        List<string> missingNewFields = GetMissingNewTransactionFields(
                            dbtr,
                            dbDoc.fileInformation?.sender,
                            DestinationCountryCode.Text);

                        if (missingNewFields.Count > 0)
                        {
                            ignoredTransactions.Add(
                                GetIgnoredTransactionMessage(
                                    dbtr.processingDetails.actionType.ToString(),
                                    dbtr.processingDetails.customerTransactionId,
                                    missingNewFields));

                            continue;
                        }

                        cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item = new SecuritiesTransactionReport4__1()
                        {
                            TxId = dbtr.processingDetails.customerTransactionId,
                            ExctgPty = dbtr.mifir.counterpartyDetails.executingEntityId.LEI,
                            InvstmtPtyInd = dbtr.mifir.counterpartyDetails.mifidInvestmentFirm==DB.YesNo.Y ? true : false,
                            SubmitgPty = dbDoc.fileInformation.sender,
                            Buyr = new PartyIdentification79__1(),
                            Sellr = new PartyIdentification79__1(),
                            OrdrTrnsmssn = new SecuritiesTransactionTransmission2() 
                            { 
                                TrnsmssnInd = dbtr.mifir.transmission.transmissionId == DB.YesNo.Y ? true : false
                            },
                            Tx = new SecuritiesTransaction1__1()
                        };

                        //////  Buyer //////
                        //Add Account Owners

                        if(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails?.Count() > 0)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr = new PartyIdentification76__1[dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails.Count()];

                            for (int ii = 0; ii < dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails.Count(); ii++)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii] = new PartyIdentification76__1();
                                string buyerBranchCountry = GetPartyBranchCountry(
                                    dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBranchCountry,
                                    dbtr.mifir.counterpartyDetails,
                                    dbtr.mifir.otherDetails);

                                if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.LEI)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.LEI;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.Item
                                        = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.Item.ToString();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = buyerBranchCountry;
                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CONCAT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CONCAT", ItemElementName = ItemChoiceType.Prtry },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = buyerBranchCountry;

                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_NIDN)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "NIDN", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = buyerBranchCountry;

                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CCPT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CCPT", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = buyerBranchCountry;

                                }


                            }

                        }


                        //Add Decision Makers

                        if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails?.Count() > 0)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr
                                = new PersonOrOrganisation2Choice__1[dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails.Count()];

                            for (int ii = 0; ii < dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails.Count(); ii++)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();

                                if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.LEI)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii].Item = BPMifir.ItemChoiceType1.LEI;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii].Item
                                        = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.Item.ToString();
                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CONCAT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CONCAT", ItemElementName = ItemChoiceType.Prtry },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionFirstname.ToUpper()
                                    };
                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_NIDN)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "NIDN", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionFirstname.ToUpper()
                                    };
                                }

                                else if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CCPT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CCPT", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionFirstname.ToUpper()
                                    };
                                }

                            };
                    

                        }

                        //////  Seller //////
                        //Add Account Owners

                        if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails?.Count() > 0)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr = new PartyIdentification76__1[dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails.Count()];

                            for (int ii = 0; ii < dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails.Count(); ii++)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii] = new PartyIdentification76__1();
                                string sellerBranchCountry = GetPartyBranchCountry(
                                    dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBranchCountry,
                                    dbtr.mifir.counterpartyDetails,
                                    dbtr.mifir.otherDetails);

                                if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.LEI)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.LEI;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.Item
                                        = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.Item.ToString();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = sellerBranchCountry;
                                }

                                else if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CONCAT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CONCAT", ItemElementName = ItemChoiceType.Prtry },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = sellerBranchCountry;

                                }

                                else if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_NIDN)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "NIDN", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = sellerBranchCountry;

                                }

                                else if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CCPT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.Prsn;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CCPT", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerFirstname.ToUpper()


                                    };

                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = sellerBranchCountry;

                                }
                            }
                        }

                        //Add Decision Makers
                        if(dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails?.Count() > 0)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr
                                = new PersonOrOrganisation2Choice__1[dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails.Count()];

                            for (int ii = 0; ii < dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails.Count(); ii++)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();

                                if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.LEI)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii].Item = BPMifir.ItemChoiceType1.LEI;
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii].Item
                                        = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.Item.ToString();
                                }

                                else if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CONCAT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CONCAT", ItemElementName = ItemChoiceType.Prtry },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionFirstname.ToUpper()
                                    };
                                }

                                if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_NIDN)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "NIDN", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionFirstname.ToUpper()
                                    };
                                }

                                if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CCPT)
                                {
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii] = new PersonOrOrganisation2Choice__1();
                                    ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.DcsnMakr[ii].Item = new PersonIdentification10__1()
                                    {
                                        Othr = new GenericPersonIdentification1__1()
                                        {
                                            SchmeNm = new PersonIdentificationSchemeName1Choice__1() { Item = "CCPT", ItemElementName = ItemChoiceType.Cd },
                                            Id = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.Item.ToString()
                                        },
                                        BirthDt = DateTime.Parse(dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionBirthdate),
                                        Nm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionSurname.ToUpper(),
                                        FrstNm = dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionFirstname.ToUpper()
                                    };
                                }

                            }
                        }


                        ////// Transactions ////


                        string tdt = dbtr.processingDetails.tradeDate + "T" + dbtr.mifir.transactionDetails.tradeTime;
                                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.TradDt
                            = DateTime.Parse(tdt).ToUniversalTime();

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.TradgCpcty
                             = (RegulatoryTradingCapacity1Code)dbtr.mifir.transactionDetails.mifirTradingCapacity;

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.TradVn
                                = dbtr.mifir.transactionDetails.venue;

                        if(dbtr.mifir.transactionDetails.venue != "XOFF"
                            && dbtr.mifir.transactionDetails.venue != "XXXX"
                            && !string.IsNullOrWhiteSpace(dbtr.mifir.otherDetails?.supervisingBranchCountry))
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.CtryOfBrnch
                                = dbtr.mifir.otherDetails.supervisingBranchCountry;

                        if(dbtr.mifir.transactionDetails.upfrontPayment!=null)
                        {
                             ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.UpFrntPmt = new AmountAndDirection53
                             {
                                 Amt = new ActiveOrHistoricCurrencyAndAmount()
                                 {
                                     Ccy = dbtr.mifir.transactionDetails.upfrontPaymentCurrency,
                                     Value = Convert.ToDecimal(dbtr.mifir.transactionDetails.upfrontPayment, new CultureInfo("en-US"))
                                 },
                                 Sgn = Convert.ToDecimal(dbtr.mifir.transactionDetails.upfrontPayment, new CultureInfo("en-US")) >= 0 ? true : false

                             };
                        }

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.CmplxTradCmpntId
                                = dbtr.mifir.transactionDetails.complexTradeComponentId;


                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric = new SecuritiesTransactionPrice4Choice();
                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item = new SecuritiesTransactionPrice2Choice();
                        if(dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.MONETARY)
                        {
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).ItemElementName = ItemChoiceType3.MntryVal;
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).Item
                                = new AmountAndDirection61
                                {
                                    Amt = new ActiveCurrencyAnd13DecimalAmount()
                                    {
                                        Ccy = dbtr.mifir.transactionDetails.priceCurrency,
                                        Value = Convert.ToDecimal(dbtr.mifir.transactionDetails.price.Item, new CultureInfo("en-US"))
                                    },
                                    Sgn = Convert.ToDecimal(dbtr.mifir.transactionDetails.price.Item, new CultureInfo("en-US")) < 0 ? false : true,
                                    SgnSpecified = false

                                };
                        }
                        else if (dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.BASIS_POINTS)
                        {
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).ItemElementName = ItemChoiceType3.BsisPts;
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).Item
                                = Convert.ToDecimal(dbtr.mifir.transactionDetails.price.Item, new CultureInfo("en-US"));
                        }
                        else if (dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.PERCENT)
                        {
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).ItemElementName = ItemChoiceType3.Pctg;
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).Item
                                = Convert.ToDecimal(dbtr.mifir.transactionDetails.price.Item, new CultureInfo("en-US"));
                        }
                        else if (dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.YIELD)
                        {
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).ItemElementName = ItemChoiceType3.Yld;
                            ((SecuritiesTransactionPrice2Choice)((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item).Item
                                = Convert.ToDecimal(dbtr.mifir.transactionDetails.price.Item, new CultureInfo("en-US"));
                        }
                        else if (dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.NOAP)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item
                                = new SecuritiesTransactionPrice1()
                                {
                                    Pdg = PriceStatus1Code.NOAP,
                                    Ccy = dbtr.mifir.transactionDetails.priceCurrency
                                };
                        }
                        else if (dbtr.mifir.transactionDetails.price.ItemElementName == DB.ItemChoiceType3.PNDG)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Pric.Item
                                = new SecuritiesTransactionPrice1()
                                {
                                    Pdg = PriceStatus1Code.PNDG,
                                    Ccy = dbtr.mifir.transactionDetails.priceCurrency
                                };
                        }


                        // Quantity
                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty = new FinancialInstrumentQuantity25Choice__1();
                        if (dbtr.mifir.transactionDetails.quantity.ItemElementName == DB.ItemChoiceType2.UNIT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.ItemElementName = ItemChoiceType2.Unit;
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.Item = Convert.ToDecimal(dbtr.mifir.transactionDetails.quantity.Item, new CultureInfo("en-US"));

                        }
                        else if (dbtr.mifir.transactionDetails.quantity.ItemElementName == DB.ItemChoiceType2.NOMINAL)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.ItemElementName = ItemChoiceType2.NmnlVal;
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.Item = new ESMA_PositiveExcludingZeroMax18()
                            { 
                                Value = Convert.ToDecimal(dbtr.mifir.transactionDetails.quantity.Item, new CultureInfo("en-US")),
                                Ccy = dbtr.mifir.transactionDetails.quantityCurrency

                            };
                        }
                        else if (dbtr.mifir.transactionDetails.quantity.ItemElementName == DB.ItemChoiceType2.MONETARY)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.ItemElementName = ItemChoiceType2.MntryVal;
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.Qty.Item = new ESMA_PositiveExcludingZeroMax18()
                            { 
                                Value = Convert.ToDecimal(dbtr.mifir.transactionDetails.quantity.Item, new CultureInfo("en-US")),
                                Ccy = dbtr.mifir.transactionDetails.quantityCurrency
                            };
                        }

                        //NetAmount
                        if (dbtr.mifir.transactionDetails.netAmount != null)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.NetAmtSpecified = true;
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Tx.NetAmt = Convert.ToDecimal(dbtr.mifir.transactionDetails.netAmount, new CultureInfo("en-US"));
                        };

                        //// Instrument ////
                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).FinInstrm = new FinancialInstrumentAttributes3Choice__1()
                        {
                            Item = dbtr.mifir.instrumentDetails.instrumentId.ISIN
                        };

                        /// Executing Person ///
                        /// 

                        if(dbtr.mifir.otherDetails?.executionId?.ItemElementName==DB.ItemChoiceType6.NATIONAL_ID_CONCAT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            string executingPersonBranchCountry = GetBranchCountry(
                                dbtr.mifir.otherDetails.supervisingBranchCountry,
                                DestinationCountryCode.Text);

                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = executingPersonBranchCountry,
                                Othr = new GenericPersonIdentification1__1()
                                {
                                    Id = dbtr.mifir.otherDetails.executionId.Item,
                                    SchmeNm = new PersonIdentificationSchemeName1Choice__1()
                                    {
                                        Item = "CONCAT",
                                        ItemElementName = ItemChoiceType.Prtry,
                                    }
                                }
                            };
                        }
                        else if (dbtr.mifir.otherDetails?.executionId?.ItemElementName == DB.ItemChoiceType6.NATIONAL_ID_CCPT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            string executingPersonBranchCountry = GetBranchCountry(
                                dbtr.mifir.otherDetails.supervisingBranchCountry,
                                DestinationCountryCode.Text);

                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = executingPersonBranchCountry,
                                Othr = new GenericPersonIdentification1__1()
                                {
                                    Id = dbtr.mifir.otherDetails.executionId.Item,
                                    SchmeNm = new PersonIdentificationSchemeName1Choice__1()
                                    {
                                        Item = "CCPT",
                                        ItemElementName = ItemChoiceType.Cd,
                                    }
                                }
                            };
                        }
                        else if (dbtr.mifir.otherDetails?.executionId?.ItemElementName == DB.ItemChoiceType6.CLIENT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new NoReasonCode();

                        }
                        else if (dbtr.mifir.otherDetails?.executionId?.ItemElementName == DB.ItemChoiceType6.ALGO)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new string("Algo");
                        };

                        /// Investment Decision ///
                        /// 
                        if (dbtr.mifir.otherDetails?.investmentDecisionId?.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CONCAT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn = new InvestmentParty1Choice__1();
                            string investmentDecisionBranchCountry = GetBranchCountry(
                                dbtr.mifir.otherDetails.investmentDecisionBranchCountry,
                                DestinationCountryCode.Text);

                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = investmentDecisionBranchCountry,
                                Othr = new GenericPersonIdentification1__1()
                                {
                                    Id = dbtr.mifir.otherDetails.investmentDecisionId.Item,
                                    SchmeNm = new PersonIdentificationSchemeName1Choice__1()
                                    {
                                        Item = "CONCAT",
                                        ItemElementName = ItemChoiceType.Prtry,
                                    }
                                }
                            };
                        }
                        else if (dbtr.mifir.otherDetails?.investmentDecisionId?.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CCPT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn = new InvestmentParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = GetBranchCountry(dbtr.mifir.otherDetails.investmentDecisionBranchCountry, DestinationCountryCode.Text),
                                Othr = new GenericPersonIdentification1__1()
                                {
                                    Id = dbtr.mifir.otherDetails.investmentDecisionId.Item,
                                    SchmeNm = new PersonIdentificationSchemeName1Choice__1()
                                    {
                                        Item = "CCPT",
                                        ItemElementName = ItemChoiceType.Cd,
                                    }
                                }
                            };
                        }
                        else if (dbtr.mifir.otherDetails?.investmentDecisionId?.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_NIDN)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn = new InvestmentParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = GetBranchCountry(dbtr.mifir.otherDetails.investmentDecisionBranchCountry, DestinationCountryCode.Text),
                                Othr = new GenericPersonIdentification1__1()
                                {
                                    Id = dbtr.mifir.otherDetails.investmentDecisionId.Item,
                                    SchmeNm = new PersonIdentificationSchemeName1Choice__1()
                                    {
                                        Item = "NIDN",
                                        ItemElementName = ItemChoiceType.Cd,
                                    }
                                }
                            };
                        };


                        /// Additional Attributes ///
                        /// 

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts = new SecuritiesTransactionIndicator2__1();

                        if (dbtr.mifir.otherDetails?.shortSellingIndicatorSpecified == true && dbtr.mifir.otherDetails?.shortSellingIndicator != null)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.ShrtSellgIndSpecified = true;
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.ShrtSellgInd =
                                dbtr.mifir.otherDetails.shortSellingIndicator == DB.mifirOtherDetailsShortSellingIndicator.SESH ? Side5Code.SESH :
                                dbtr.mifir.otherDetails.shortSellingIndicator == DB.mifirOtherDetailsShortSellingIndicator.SELL ? Side5Code.SELL :
                                dbtr.mifir.otherDetails.shortSellingIndicator == DB.mifirOtherDetailsShortSellingIndicator.SSEX ? Side5Code.SSEX :
                                dbtr.mifir.otherDetails.shortSellingIndicator == DB.mifirOtherDetailsShortSellingIndicator.UNDI ? Side5Code.UNDI : Side5Code.UNDI;
                        };

                        if (dbtr.mifir.otherDetails?.waiverIndicator!=null)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.WvrInd = new ReportingWaiverType1Code[1];
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.WvrInd[0] =
                                dbtr.mifir.otherDetails.waiverIndicator == "OILQ" ? ReportingWaiverType1Code.OILQ :
                                dbtr.mifir.otherDetails.waiverIndicator == "NLIQ" ? ReportingWaiverType1Code.NLIQ :
                                dbtr.mifir.otherDetails.waiverIndicator == "PRIC" ? ReportingWaiverType1Code.PRIC :
                                dbtr.mifir.otherDetails.waiverIndicator == "ILQD" ? ReportingWaiverType1Code.ILQD :
                                dbtr.mifir.otherDetails.waiverIndicator == "RFPT" ? ReportingWaiverType1Code.RFPT :
                                dbtr.mifir.otherDetails.waiverIndicator == "SIZE" ? ReportingWaiverType1Code.SIZE : ReportingWaiverType1Code.OILQ;
                        };
                        if (dbtr.mifir.otherDetails?.otcPostTradeIndicator != null)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.OTCPstTradInd = new ReportingWaiverType3Code[1];
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.OTCPstTradInd[0] =
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "BENC" ? ReportingWaiverType3Code.BENC :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "ACTX" ? ReportingWaiverType3Code.ACTX :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "ILQD" ? ReportingWaiverType3Code.ILQD :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "SIZE" ? ReportingWaiverType3Code.SIZE :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "CANC" ? ReportingWaiverType3Code.CANC :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "AMND" ? ReportingWaiverType3Code.AMND :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "SDIV" ? ReportingWaiverType3Code.SDIV :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "RPRI" ? ReportingWaiverType3Code.RPRI :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "DUPL" ? ReportingWaiverType3Code.DUPL :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "LRGS" ? ReportingWaiverType3Code.LRGS :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "TNCP" ? ReportingWaiverType3Code.TNCP :
                                dbtr.mifir.otherDetails.otcPostTradeIndicator == "TPAC" ? ReportingWaiverType3Code.TPAC : ReportingWaiverType3Code.BENC;

                        };
                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.SctiesFincgTxInd
                            = dbtr.mifir.otherDetails?.sftrIndicatorSpecified == true
                                && dbtr.mifir.otherDetails.sftrIndicator == DB.YesNo.Y;

                    }

                    cyDoc.Pyld.Document.FinInstrmRptgTxRpt = cyDoc.Pyld.Document.FinInstrmRptgTxRpt
                        .Where(report => report?.Item != null)
                        .ToArray();

                    if (ignoredTransactions.Count > 0)
                    {
                        MessageBoxResult ignoredResult = ShowIgnoredTransactionsDialog(ignoredTransactions);

                        if (ignoredResult != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    if (cyDoc.Pyld.Document.FinInstrmRptgTxRpt.Length == 0)
                    {
                        MessageBox.Show(
                            "No ESMA transaction reports were generated because all input transactions were missing mandatory fields.",
                            "Processing Error");
                        return;
                    }



                    SaveFileDialog saveFileDialog = new SaveFileDialog();
                    saveFileDialog.DefaultExt = ".xml"; // Required file extension 
                    saveFileDialog.FileName = BizMsgIdr + ".xml";
                    saveFileDialog.Filter = "Xml documents (.xml)|*.xml";

                    if (saveFileDialog.ShowDialog() == true)
                    {

                        XmlSerializer writer = new System.Xml.Serialization.XmlSerializer(typeof(BizData));

                        using (var fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write))
                        {
                            writer.Serialize(fileStream, cyDoc);
                        }

                        MessageBox.Show("Successfully generated "+ saveFileDialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error processing" + "\n" + ex?.Message, "Processing Error");
                    return;
                }

            }
        }

        private MessageBoxResult ShowIgnoredTransactionsDialog(IEnumerable<string> ignoredTransactions)
        {
            List<string> ignoredTransactionList = ignoredTransactions.ToList();

            Window dialog = new Window()
            {
                Title = "Missing Mandatory Fields",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxHeight = 500,
                MaxWidth = 760,
                MinWidth = 560,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            Grid layout = new Grid()
            {
                Margin = new Thickness(18)
            };
            layout.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            TextBlock warningIcon = new TextBlock()
            {
                Text = "!",
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 14, 0),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.White,
                Background = Brushes.DarkOrange
            };
            Grid.SetColumn(warningIcon, 0);
            Grid.SetRow(warningIcon, 0);
            layout.Children.Add(warningIcon);

            TextBlock message = new TextBlock()
            {
                Text = "The following transactions will be ignored because mandatory fields are missing from the input file.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 660,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetColumn(message, 1);
            Grid.SetRow(message, 0);
            layout.Children.Add(message);

            ListBox transactionList = new ListBox()
            {
                ItemsSource = ignoredTransactionList,
                MaxHeight = 260,
                MinHeight = 100,
                MaxWidth = 660,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(transactionList, 1);
            Grid.SetRow(transactionList, 1);
            layout.Children.Add(transactionList);

            StackPanel buttons = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            Button copyButton = new Button()
            {
                Content = CreateCopyButtonContent("Copy"),
                Width = 92,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Button yesButton = new Button()
            {
                Content = "Yes",
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            Button noButton = new Button()
            {
                Content = "No",
                Width = 80,
                IsCancel = true
            };

            MessageBoxResult result = MessageBoxResult.No;
            copyButton.Click += (_, _) =>
            {
                Clipboard.SetText(string.Join(Environment.NewLine, ignoredTransactionList));
                copyButton.Content = CreateCopyButtonContent("Copied");
            };
            yesButton.Click += (_, _) =>
            {
                result = MessageBoxResult.Yes;
                dialog.Close();
            };
            noButton.Click += (_, _) =>
            {
                result = MessageBoxResult.No;
                dialog.Close();
            };

            buttons.Children.Add(copyButton);
            buttons.Children.Add(yesButton);
            buttons.Children.Add(noButton);

            Grid.SetColumn(buttons, 1);
            Grid.SetRow(buttons, 2);
            layout.Children.Add(buttons);

            dialog.Content = layout;
            dialog.ShowDialog();

            return result;
        }

        private static StackPanel CreateCopyButtonContent(string text)
        {
            StackPanel content = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Grid icon = new Grid()
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 6, 0)
            };

            Border backPage = new Border()
            {
                Width = 9,
                Height = 10,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            Border frontPage = new Border()
            {
                Width = 9,
                Height = 10,
                BorderBrush = Brushes.DimGray,
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            icon.Children.Add(backPage);
            icon.Children.Add(frontPage);
            content.Children.Add(icon);
            content.Children.Add(new TextBlock() { Text = text });

            return content;
        }

        private static string GetIgnoredTransactionMessage(
            string transactionType,
            string transactionId,
            IEnumerable<string> missingFields)
        {
            string displayTransactionId = string.IsNullOrWhiteSpace(transactionId)
                ? "(missing transaction reference)"
                : transactionId;

            return $"{transactionType}: {displayTransactionId} - missing {string.Join(", ", missingFields)}";
        }

        private static List<string> GetMissingNewTransactionFields(
            DB.transactionPositionReport dbtr,
            string submittingParty,
            string destinationCountry)
        {
            List<string> missing = new List<string>();

            AddMissingField(missing, "Transaction reference number", dbtr?.processingDetails?.customerTransactionId);
            AddMissingField(missing, "Trade date", dbtr?.processingDetails?.tradeDate);

            AddMissingField(missing, "Submitting entity identification code", submittingParty);

            DB.mifirDetails mifir = dbtr?.mifir;
            DB.mifirCounterpartyDetails counterparty = mifir?.counterpartyDetails;
            DB.mifirTransaction transaction = mifir?.transactionDetails;
            DB.mifirInstrumentDetails instrument = mifir?.instrumentDetails;
            DB.mifirOtherDetails otherDetails = mifir?.otherDetails;

            AddMissingField(missing, "Executing entity identification code", counterparty?.executingEntityId?.LEI);
            AddMissingField(missing, "Investment firm indicator", counterparty?.mifidInvestmentFirmSpecified == true ? "present" : null);
            AddMissingField(missing, "Buyer details", counterparty?.buyer?.mifirBuyerDetails?.Length > 0 ? "present" : null);
            AddMissingField(missing, "Seller details", counterparty?.seller?.mifirSellerDetails?.Length > 0 ? "present" : null);

            AddMissingBuyerDetails(missing, counterparty, otherDetails);
            AddMissingSellerDetails(missing, counterparty, otherDetails);

            AddMissingField(missing, "Transmission of order indicator", mifir?.transmission?.transmissionIdSpecified == true ? "present" : null);
            AddMissingField(missing, "Trade time", transaction?.tradeTime);
            AddMissingField(missing, "Trading capacity", transaction?.mifirTradingCapacitySpecified == true ? "present" : null);
            AddMissingField(missing, "Trading venue", transaction?.venue);
            AddMissingPriceFields(missing, transaction);
            AddMissingQuantityFields(missing, transaction);
            AddInvalidDecimal(missing, "Net amount", transaction?.netAmount, required: false);

            if (!string.IsNullOrWhiteSpace(transaction?.upfrontPayment))
            {
                AddMissingField(missing, "Upfront payment currency", transaction.upfrontPaymentCurrency);
                AddInvalidDecimal(missing, "Upfront payment", transaction.upfrontPayment, required: true);
            }

            string tradeDateTime = dbtr?.processingDetails?.tradeDate + "T" + transaction?.tradeTime;
            if (!string.IsNullOrWhiteSpace(dbtr?.processingDetails?.tradeDate)
                && !string.IsNullOrWhiteSpace(transaction?.tradeTime)
                && !DateTime.TryParse(tradeDateTime, out _))
            {
                missing.Add("Valid trade date/time");
            }

            AddMissingField(missing, "Instrument identifier", instrument?.instrumentId?.ISIN);

            AddMissingExecutionDecisionFields(missing, otherDetails, destinationCountry);
            AddMissingInvestmentDecisionFields(missing, otherDetails, destinationCountry);

            return missing;
        }

        private static IEnumerable<string> GetMissingFields(params (string Name, string Value)[] fields)
        {
            return fields
                .Where(field => string.IsNullOrWhiteSpace(field.Value))
                .Select(field => field.Name);
        }

        private static void AddMissingBuyerDetails(
            List<string> missing,
            DB.mifirCounterpartyDetails counterparty,
            DB.mifirOtherDetails otherDetails)
        {
            DB.mifirBuyer buyer = counterparty?.buyer;

            if (buyer?.mifirBuyerDetails == null)
            {
                return;
            }

            for (int i = 0; i < buyer.mifirBuyerDetails.Length; i++)
            {
                DB.mifirBuyerDetails details = buyer.mifirBuyerDetails[i];
                string prefix = $"Buyer {i + 1}";

                AddMissingField(missing, $"{prefix} identifier", details?.buyerId?.Item?.ToString());
                if (details?.buyerId != null && IsNaturalPersonId(details.buyerId.ItemElementName))
                {
                    AddPersonFields(missing, prefix, details.buyerFirstname, details.buyerSurname, details.buyerBirthdate);
                }
            }

            AddMissingBuyerDecisionMakerDetails(missing, buyer);
        }

        private static void AddMissingSellerDetails(
            List<string> missing,
            DB.mifirCounterpartyDetails counterparty,
            DB.mifirOtherDetails otherDetails)
        {
            DB.mifirSeller seller = counterparty?.seller;

            if (seller?.mifirSellerDetails == null)
            {
                return;
            }

            for (int i = 0; i < seller.mifirSellerDetails.Length; i++)
            {
                DB.mifirSellerDetails details = seller.mifirSellerDetails[i];
                string prefix = $"Seller {i + 1}";

                AddMissingField(missing, $"{prefix} identifier", details?.sellerId?.Item?.ToString());
                if (details?.sellerId != null && IsNaturalPersonId(details.sellerId.ItemElementName))
                {
                    AddPersonFields(missing, prefix, details.sellerFirstname, details.sellerSurname, details.sellerBirthdate);
                }
            }

            AddMissingSellerDecisionMakerDetails(missing, seller);
        }

        private static void AddMissingBuyerDecisionMakerDetails(List<string> missing, DB.mifirBuyer buyer)
        {
            if (buyer?.mifirBuyerDecisionMakerDetails == null)
            {
                return;
            }

            for (int i = 0; i < buyer.mifirBuyerDecisionMakerDetails.Length; i++)
            {
                DB.mifirBuyerDecisionMakerDetails details = buyer.mifirBuyerDecisionMakerDetails[i];
                string prefix = $"Buyer decision maker {i + 1}";

                AddMissingField(missing, $"{prefix} identifier", details?.buyerDecisionMakerId?.Item);

                if (details?.buyerDecisionMakerId != null
                    && IsNaturalPersonId(details.buyerDecisionMakerId.ItemElementName))
                {
                    AddPersonFields(missing, prefix, details.buyerDecisionFirstname, details.buyerDecisionSurname, details.buyerDecisionBirthdate);
                }
            }
        }

        private static void AddMissingSellerDecisionMakerDetails(List<string> missing, DB.mifirSeller seller)
        {
            if (seller?.mifirSellerDecisionMakerDetails == null)
            {
                return;
            }

            for (int i = 0; i < seller.mifirSellerDecisionMakerDetails.Length; i++)
            {
                DB.mifirSellerDecisionMakerDetails details = seller.mifirSellerDecisionMakerDetails[i];
                string prefix = $"Seller decision maker {i + 1}";

                AddMissingField(missing, $"{prefix} identifier", details?.sellerDecisionMakerId?.Item);

                if (details?.sellerDecisionMakerId != null
                    && IsNaturalPersonId(details.sellerDecisionMakerId.ItemElementName))
                {
                    AddPersonFields(missing, prefix, details.sellerDecisionFirstname, details.sellerDecisionSurname, details.sellerDecisionBirthdate);
                }
            }
        }

        private static void AddMissingPriceFields(List<string> missing, DB.mifirTransaction transaction)
        {
            AddMissingField(missing, "Price", transaction?.price?.Item?.ToString());

            if (transaction?.price == null)
            {
                return;
            }

            if (transaction.price.ItemElementName == DB.ItemChoiceType3.MONETARY)
            {
                AddMissingField(missing, "Price currency", transaction.priceCurrency);
            }

            if (transaction.price.ItemElementName == DB.ItemChoiceType3.NOAP
                || transaction.price.ItemElementName == DB.ItemChoiceType3.PNDG)
            {
                return;
            }

            AddInvalidDecimal(missing, "Price", transaction.price.Item?.ToString(), required: true);
        }

        private static void AddMissingQuantityFields(List<string> missing, DB.mifirTransaction transaction)
        {
            AddMissingField(missing, "Quantity", transaction?.quantity?.Item?.ToString());

            if (transaction?.quantity == null)
            {
                return;
            }

            if (transaction.quantity.ItemElementName == DB.ItemChoiceType2.NOMINAL
                || transaction.quantity.ItemElementName == DB.ItemChoiceType2.MONETARY)
            {
                AddMissingField(missing, "Quantity currency", transaction.quantityCurrency);
            }

            AddInvalidDecimal(missing, "Quantity", transaction.quantity.Item?.ToString(), required: true);
        }

        private static void AddMissingExecutionDecisionFields(
            List<string> missing,
            DB.mifirOtherDetails otherDetails,
            string destinationCountry)
        {
            if (otherDetails?.executionId == null)
            {
                return;
            }

            if (otherDetails.executionId.ItemElementName == DB.ItemChoiceType6.NATIONAL_ID_CONCAT
                || otherDetails.executionId.ItemElementName == DB.ItemChoiceType6.NATIONAL_ID_CCPT)
            {
                AddMissingField(
                    missing,
                    "Executing person branch country or destination country fallback",
                    GetBranchCountry(otherDetails.supervisingBranchCountry, destinationCountry));
            }
        }

        private static void AddMissingInvestmentDecisionFields(
            List<string> missing,
            DB.mifirOtherDetails otherDetails,
            string destinationCountry)
        {
            if (otherDetails?.investmentDecisionId == null)
            {
                return;
            }

            if (otherDetails.investmentDecisionId.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CONCAT)
            {
                AddMissingField(
                    missing,
                    "Investment decision branch country or destination country fallback",
                    GetBranchCountry(otherDetails.investmentDecisionBranchCountry, destinationCountry));
            }

            if (otherDetails.investmentDecisionId.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CCPT
                || otherDetails.investmentDecisionId.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_NIDN)
            {
                AddMissingField(
                    missing,
                    "Investment decision branch country or destination country fallback",
                    GetBranchCountry(otherDetails.investmentDecisionBranchCountry, destinationCountry));
            }
        }

        private static void AddPersonFields(
            List<string> missing,
            string prefix,
            string firstName,
            string surname,
            string birthDate)
        {
            AddMissingField(missing, $"{prefix} first name", firstName);
            AddMissingField(missing, $"{prefix} surname", surname);
            AddMissingField(missing, $"{prefix} birth date", birthDate);

            if (!string.IsNullOrWhiteSpace(birthDate) && !DateTime.TryParse(birthDate, out _))
            {
                missing.Add($"{prefix} valid birth date");
            }
        }

        private static string GetPartyBranchCountry(
            string partyBranchCountry,
            DB.mifirCounterpartyDetails counterparty,
            DB.mifirOtherDetails otherDetails)
        {
            if (!string.IsNullOrWhiteSpace(partyBranchCountry))
            {
                return partyBranchCountry;
            }

            if (!string.IsNullOrWhiteSpace(counterparty?.branchMembershipCountry))
            {
                return counterparty.branchMembershipCountry;
            }

            if (!string.IsNullOrWhiteSpace(otherDetails?.supervisingBranchCountry))
            {
                return otherDetails.supervisingBranchCountry;
            }

            return null;
        }

        private static string GetBranchCountry(string branchCountry, string fallbackCountry)
        {
            return !string.IsNullOrWhiteSpace(branchCountry)
                ? branchCountry
                : fallbackCountry;
        }

        private static bool IsNaturalPersonId(DB.ItemChoiceType itemChoiceType)
        {
            return itemChoiceType == DB.ItemChoiceType.NATIONAL_ID_CCPT
                || itemChoiceType == DB.ItemChoiceType.NATIONAL_ID_CONCAT
                || itemChoiceType == DB.ItemChoiceType.NATIONAL_ID_DERCON
                || itemChoiceType == DB.ItemChoiceType.NATIONAL_ID_NIDN;
        }

        private static bool IsNaturalPersonId(DB.ItemChoiceType1 itemChoiceType)
        {
            return itemChoiceType == DB.ItemChoiceType1.NATIONAL_ID_CCPT
                || itemChoiceType == DB.ItemChoiceType1.NATIONAL_ID_CONCAT
                || itemChoiceType == DB.ItemChoiceType1.NATIONAL_ID_DERCON
                || itemChoiceType == DB.ItemChoiceType1.NATIONAL_ID_NIDN;
        }

        private static void AddMissingField(List<string> missing, string fieldName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(fieldName);
            }
        }

        private static void AddInvalidDecimal(List<string> missing, string fieldName, string value, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    missing.Add(fieldName);
                }

                return;
            }

            if (!decimal.TryParse(value, NumberStyles.Number, new CultureInfo("en-US"), out _))
            {
                missing.Add($"Valid {fieldName.ToLower()}");
            }
        }

        private void Terms_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Use at yourown risk.\nThis is a free test application and should not be used in production.\n" +
                "The developer makes no guarantees, express or implied and does not assume any responsibility" +
                "for the accuracy of the results or any damages that may result from the use of this application.", "Terms of Use");
            return;
        }
    }


}
