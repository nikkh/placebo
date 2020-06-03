IF  NOT EXISTS (SELECT * FROM sys.objects 
WHERE object_id = OBJECT_ID(N'[dbo].[Invoice]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Invoice](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceNumber] [nvarchar](50) NOT NULL,
	[TaxDate] [datetime2](7) NULL,
	[OrderNumber] [nvarchar](50) NULL,
	[OrderDate] [datetime2](7) NULL,
	[FileName] [nvarchar](50) NULL,
	[ShreddingUtcDateTime] [datetime2](7) NOT NULL,
	[TimeToShred] [bigint] NOT NULL,
	[RecognizerStatus] [nvarchar](50) NULL,
	[RecognizerErrors] [nvarchar](50) NULL,
	[UniqueRunIdentifier] [nvarchar](50) NOT NULL,
	[TerminalErrorCount] [int] NOT NULL,
	[WarningErrorCount] [int] NOT NULL,
	[IsValid] [bit] NOT NULL,
	[Account] [nvarchar](50) NULL,
	[VatAmount] [decimal](19, 5) NULL,
	[NetTotal] [decimal](19, 5) NULL,
	[GrandTotal] [decimal](19, 5) NULL,
	[PostCode] [nvarchar](10) NULL,
	[Thumbprint] [nvarchar](50) NULL,
	[TaxPeriod] [nvarchar](6) NULL,
	[ModelId] [nvarchar](50) NULL,
	[ModelVersion] [nvarchar](50) NULL,

PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END

IF  NOT EXISTS (SELECT * FROM sys.objects 
WHERE object_id = OBJECT_ID(N'[dbo].[InvoiceError]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[InvoiceError](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[ErrorCode] [nvarchar](10) NULL,
	[ErrorSeverity] [nvarchar](10) NULL,
	[ErrorMessage] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END


IF  NOT EXISTS (SELECT * FROM sys.objects 
WHERE object_id = OBJECT_ID(N'[dbo].[InvoiceLineItem]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[InvoiceLineItem](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[InvoiceId] [int] NOT NULL,
	[InvoiceLineNumber] [nvarchar](5) NOT NULL,
	[ItemDescription] [nvarchar](max) NULL,
	[LineQuantity] [nvarchar](50) NULL,
	[UnitPrice] [decimal](19, 5) NULL,
	[VATCode] [nvarchar](50) NULL,
	[NetAmount] [decimal](19, 5) NULL,
	[CalculatedLineQuantity] [decimal](18, 0) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END

IF  NOT EXISTS (SELECT * FROM sys.objects 
WHERE object_id = OBJECT_ID(N'[dbo].[ModelTraining]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ModelTraining](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DocumentFormat] [nvarchar](15) NOT NULL,
	[ModelVersion] [int] NOT NULL,
	[ModelId] [nvarchar](50) NOT NULL,
	[CreatedDateTime] [datetime2](7) NOT NULL,
	[UpdatedDateTime] [datetime2](7) NOT NULL,
	[BlobSasUrl] [nvarchar](max) NOT NULL,
	[BlobFolderName] [nvarchar](50) NULL,
	[IncludeSubfolders] [bit] NOT NULL,
	[UseLabelFile] [bit] NOT NULL,
	[AverageModelAccuracy] [decimal](19, 5) NOT NULL,
	[TrainingDocumentResults] [nvarchar](max) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END

IF OBJECT_ID('[dbo].[GetModelByDocumentFormat]', 'P') IS NOT NULL  
   DROP PROCEDURE [dbo].[GetModelByDocumentFormat];  
GO  
CREATE PROCEDURE [dbo].[GetModelByDocumentFormat]  @DocumentFormat VARCHAR(15)   
AS    
 
   SET NOCOUNT ON;  
   SELECT DocumentFormat, ModelId, ModelVersion, UpdatedDateTime, AverageModelAccuracy 
	FROM  ModelTraining 
	WHERE ModelVersion=(SELECT max(ModelVersion) FROM ModelTraining WHERE DocumentFormat=@DocumentFormat) AND DocumentFormat=@DocumentFormat
   
RETURN
GO