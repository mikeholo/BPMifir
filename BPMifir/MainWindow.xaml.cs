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
using Windows.Storage;


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
                    
                    //Add Transactions
                    for (int i = 0; i< dbDoc.record.Items.Count(); i++)
                    {
                        cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i] = new ReportingTransactionType1Choice__1();
                        DB.transactionPositionReport dbtr = (DB.transactionPositionReport)dbDoc.record.Items[i];
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
                    
                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr 
                            = new PartyIdentification76__1[dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails.Count()];
                    
                        for(int ii = 0; ii < dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails.Count(); ii++)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii] = new PartyIdentification76__1();

                            if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.LEI)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();             
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.LEI;
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].Id.Item
                                    = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.Item.ToString();
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBranchCountry;
                            }

                            if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CONCAT)
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
                                    BirthDt = DateTime.Parse( dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBirthdate),
                                    Nm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerSurname.ToUpper(),
                                    FrstNm = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerFirstname.ToUpper()
                                

                                };

                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Buyr.AcctOwnr[ii].CtryOfBrnch = dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDetails[ii].buyerBranchCountry;

                            }
                        }

                        //Add Decision Makers

                        if(dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails?.Count() > 0)
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

                                if (dbtr.mifir.counterpartyDetails.buyer.mifirBuyerDecisionMakerDetails[ii].buyerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CONCAT)
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
                            };
                    

                        }

                        //////  Seller //////
                        //Add Account Owners

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr
                            = new PartyIdentification76__1[dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails.Count()];

                        for (int ii = 0; ii < dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails.Count(); ii++)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii] = new PartyIdentification76__1();

                            if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.LEI)
                            {
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id = new PersonOrOrganisation1Choice__1();
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.ItemElementName = BPMifir.ItemChoiceType1.LEI;
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].Id.Item
                                    = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.Item.ToString();
                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBranchCountry;
                            }

                            if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerId.ItemElementName == DB.ItemChoiceType.NATIONAL_ID_CONCAT)
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

                                ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).Sellr.AcctOwnr[ii].CtryOfBrnch = dbtr.mifir.counterpartyDetails.seller.mifirSellerDetails[ii].sellerBranchCountry;

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

                                if (dbtr.mifir.counterpartyDetails.seller.mifirSellerDecisionMakerDetails[ii].sellerDecisionMakerId.ItemElementName == DB.ItemChoiceType1.NATIONAL_ID_CONCAT)
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

                        if(dbtr.mifir.otherDetails.executionId?.ItemElementName==DB.ItemChoiceType6.NATIONAL_ID_CONCAT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();

                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = DestinationCountryCode.Text,
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
                        else if (dbtr.mifir.otherDetails.executionId?.ItemElementName == DB.ItemChoiceType6.NATIONAL_ID_CCPT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();

                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch =  DestinationCountryCode.Text,
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
                        else if (dbtr.mifir.otherDetails.executionId?.ItemElementName == DB.ItemChoiceType6.CLIENT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new NoReasonCode();

                        }
                        else if (dbtr.mifir.otherDetails.executionId?.ItemElementName == DB.ItemChoiceType6.ALGO)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn = new ExecutingParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).ExctgPrsn.Item = new string("Algo");
                        };

                        /// Investment Decision ///
                        /// 
                        if (dbtr.mifir.otherDetails.investmentDecisionId?.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CONCAT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn = new InvestmentParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = DestinationCountryCode.Text,
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
                        else if (dbtr.mifir.otherDetails.investmentDecisionId?.ItemElementName == DB.ItemChoiceType5.NATIONAL_ID_CCPT)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn = new InvestmentParty1Choice__1();
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).InvstmtDcsnPrsn.Item = new PersonIdentification12__1()
                            {
                                CtryOfBrnch = !string.IsNullOrEmpty(dbtr.mifir.otherDetails.investmentDecisionBranchCountry) ? dbtr.mifir.otherDetails.investmentDecisionBranchCountry : DestinationCountryCode.Text,
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
                        };


                        /// Additional Attributes ///
                        /// 

                        ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts = new SecuritiesTransactionIndicator2__1();

                        if(dbtr.mifir.otherDetails?.shortSellingIndicator != null)
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
                        if (dbtr.mifir.otherDetails?.sftrIndicator != null)
                        {
                            ((SecuritiesTransactionReport4__1)(cyDoc.Pyld.Document.FinInstrmRptgTxRpt[i].Item)).AddtlAttrbts.SctiesFincgTxInd = dbtr.mifir.otherDetails?.sftrIndicator == DB.YesNo.Y ? true : false;
                        }

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

        private void Terms_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Use at yourown risk.\nThis is a test application and should not be used in production.\n" +
                "The developer makes no guarantees, express or implied and does not assumes any responsibility" +
                "for the accuracy of the results or any damages that may result from the use of this application.", "Terms of Use");
            return;
        }
    }


}
