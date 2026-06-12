# Regulatory Format Notes

This application translates an upstream MiFIR transaction-reporting XML file into an ESMA ISO 20022 transaction-reporting XML file. The code currently models the upstream file as `DB.reportFile` in the `http://deutsche-boerse.com/DBRegHub` namespace and emits an ESMA `BizData` envelope carrying an `auth.016.001.01` transaction-data message.

These notes are implementation context for maintainers. They are not legal, compliance, or regulatory advice.

For the agreed product redesign, archive model, recovery approach, and DORA/DPIA-oriented operating controls, see `docs/TargetArchitecture.md`.

## Source Context

MiFIR Article 26 requires investment firms that execute reportable transactions to report complete and accurate transaction details to the competent authority as quickly as possible and no later than the close of the following working day. The report can be submitted by the investment firm, an Approved Reporting Mechanism (ARM), or the trading venue through whose system the transaction was completed. Investment firms remain responsible for completeness, accuracy, and timely submission except for failures attributable to an ARM or trading venue, and errors or omissions must be corrected.

ESMA's transaction-reporting technical framework is TREM, the Transaction Reporting Exchange Mechanism. ESMA describes TREM as covering transaction reports to competent authorities under Article 26 of MiFIR and the related technical standards. ESMA's transaction-reporting documents include guidelines, validation rules, message schemas, and technical reporting instructions.

The upstream format in this code is labelled `DB` in the C# namespace, and the XML namespace and generated model indicate Deutsche Boerse Regulatory Reporting Hub (`DBRegHub`). Deutsche Boerse publicly describes its Regulatory Reporting Hub as a platform for MiFID II/MiFIR reporting solutions and European reporting to national competent authorities.

## Upstream DBRegHub XML

The generated input model in `BPMifir/Models/DBModel.cs` expects:

- Root element: `reportFile`.
- Namespace: `http://deutsche-boerse.com/DBRegHub`.
- File header: `fileInformation`, including `sender`, `timestamp`, `environment`, and `version`.
- Record container: `record`.
- Transaction records: `transactionPositionReport` items serialized as `transaction` entries under `record`.
- Main transaction sections: `processingDetails`, optional `configurableFields`, and `mifir`.

The input model contains MiFIR-oriented parties, instrument, transaction, and other-details structures. Relevant identifiers include `LEI`, `MIC`, `INTC`, `CLIENT`, `ALGO`, `INTERNAL_PARTY_ID`, and national-person identifier variants such as `NATIONAL_ID_CONCAT`, `NATIONAL_ID_NIDN`, `NATIONAL_ID_CCPT`, and `NATIONAL_ID_DERCON`.

Common input fields used by the translator include:

- `fileInformation.sender`: mapped as ESMA submitting party.
- `processingDetails.customerTransactionId`: mapped as ESMA transaction ID.
- `mifir.counterpartyDetails.executingEntityId.LEI`: mapped as executing party.
- Buyer/seller account-owner and decision-maker details.
- Investment and execution decision identifiers.
- Transaction date/time, trading capacity, venue, branch country, price, quantity, net amount, and complex-trade component ID.
- Financial instrument attributes and underlying instrument/index details.
- Short-selling, waiver, OTC post-trade, and SFTR indicators.

## ESMA Transaction Reporting XML

The generated output model in `BPMifir/Models/ESMAReportModel.cs` emits an ISO 20022-style ESMA business file:

- Outer envelope: `BizData`.
- Envelope namespace: `urn:iso:std:iso:20022:tech:xsd:head.003.001.01`.
- Business Application Header: `AppHdr`.
- BAH namespace: `urn:iso:std:iso:20022:tech:xsd:head.001.001.01`.
- Message definition identifier: `auth.016.001.01`.
- Payload document namespace in the current model: `urn:iso:std:iso:20022:tech:xsd:DRAFT15auth.016.001.01`.
- Payload root: `Document`.
- Transaction report collection: `FinInstrmRptgTxRpt`.
- New transaction record type: `SecuritiesTransactionReport4__1`.
- Cancellation record type: `SecuritiesTransactionReport2__1`.

ESMA's technical reporting instructions describe the Business Application Header (`head.001.001.01`) as an ISO 20022 message definition that can be combined with an ISO 20022 business message. The application header and the business message are packaged inside a business file header/envelope to form the XML file. This matches the application's `BizData -> Hdr/AppHdr -> Pyld/Document` object graph.

The application constructs:

- `Fr` from the reporting entity code entered in the UI.
- `To` from the destination country code entered in the UI.
- `BizMsgIdr` from reporting entity, message type marker, destination, sequence, version, previous sequence, and year.
- `MsgDefIdr` as `auth.016.001.01`.
- `CreDt` as the current UTC timestamp.

## Regulatory Data Content

MiFIR Article 26 says reports include, among other things, financial instrument identifiers, quantity, execution date and time, effective dates, transaction price, identity of the client, parties on whose behalf the transaction was executed, people or algorithms responsible for investment and execution decisions, the entity subject to the reporting obligation, and identification of the investment firms concerned.

RTS 22, Commission Delegated Regulation (EU) 2017/590, specifies data standards and formats for transaction reporting. Its Annex I Table 2 is the canonical field-level source for transaction-report content. The current ESMA implementation uses XML schema version 1.1.0 for MiFIR transaction reporting, in use since 23 September 2019 according to ESMA's MiFIR Reporting page.

The translator should preserve the following reporting concepts when mapping from DBRegHub to ESMA:

- Transaction identity and lifecycle: new versus cancellation, transaction reference, venue transaction ID where applicable, and complex-trade component ID.
- Parties: executing party, submitting party, buyer, seller, account owners, decision makers, and branches.
- Person identifiers: LEI for legal persons and applicable national-person identifier scheme for natural persons. The current code maps `CONCAT`, `NIDN`, and `CCPT` variants into ESMA person-identification structures.
- Firm decision data: investment decision person/algorithm and execution person/algorithm.
- Instrument data: ISIN or derivative/underlying information, classification, maturity/expiry fields, and underlying instrument/index where required.
- Transaction economics: trade date/time, quantity, price type, price currency, net amount, venue, trading capacity, and country of branch.
- Regulatory flags: short-selling, waiver, OTC post-trade, securities-financing-transaction indicator, order transmission indicator, and investment-firm indicator.

## Validation and Feedback Implications

ESMA's technical reporting instructions distinguish file validation from content validation:

- File validation checks XML schema compliance. A file-level XML schema error rejects the whole file.
- Content validation checks individual transaction reports. Incorrect records can be rejected while correct records in the same file continue through the process.
- Content validations include instrument-reference-data validations, such as ISIN and underlying checks against FIRDS data.
- If an otherwise valid record depends on missing instrument reference data, ESMA describes a pending path with repeated validation until the seventh calendar day after receipt.
- Feedback files include accepted, rejected, and pending statuses and relevant validation-rule information.

This application currently translates and serializes XML. It should not be assumed to perform full ESMA content validation unless explicit validation code is added. Schema validation also depends on shipping and loading the exact ESMA and upstream XSD files used by the target competent authority or ARM.

## Version and Schema Notes

The ESMA public MiFIR transaction reporting page states that transaction reporting XML schema version 1.1.0 has applied since 23 September 2019. It also links the transaction-reporting schemas, updated reporting instructions, and validation rules.

The current output model still uses the namespace string `DRAFT15auth.016.001.01` and schema file name `DRAFT15auth.016.001.01_ESMAUG_Reporting_1.0.3.xsd`. Before production use, confirm that the target authority or ARM still accepts that namespace and schema version. If the destination expects the registered post-draft schema package, regenerate `ESMAReportModel.cs` from the accepted XSDs and update the serializer namespace mappings.

The DBRegHub model was generated from an XSD but the source XSD is not present in the repository. The model should be treated as schema-derived, and any input-format changes should be made by regenerating from the authoritative DBRegHub XSD rather than manually editing the generated classes.

## Useful Source Links

- ESMA MiFIR Reporting: https://www.esma.europa.eu/data-reporting/mifir-reporting
- ESMA Article 26 interactive rulebook entry: https://www.esma.europa.eu/publications-and-data/interactive-single-rulebook/mifir/article-26-obligation-report-transactions
- ESMA MiFIR Transaction Reporting Technical Reporting Instructions: https://www.esma.europa.eu/sites/default/files/library/esma65-8-2356_mifir_transaction_reporting_technical_reporting_instructions.pdf
- ESMA Transaction Reporting Message Schemas: https://www.esma.europa.eu/document/transaction-reporting-message-schemas
- Deutsche Boerse Regulatory Reporting Hub MiFID II/MiFIR overview: https://www.deutsche-boerse.com/dbg-de/ueber-uns/regulierung/regulation-trading-clearing-data/mifid-mifir/Deutsche-B-rse-bietet-Reporting-L-sungen-f-r-MiFID-II-MiFIR-143710
