IF  NOT EXISTS (SELECT * FROM sys.objects 
WHERE object_id = OBJECT_ID(N'[dbo].[Invoice]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Document](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DocumentNumber] [nvarchar](50) NOT NULL,
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
WHERE object_id = OBJECT_ID(N'[dbo].[DocumentError]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[DocumentError](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DocumentId] [int] NOT NULL,
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
WHERE object_id = OBJECT_ID(N'[dbo].[DocumentLineItem]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[DocumentLineItem](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DocumentId] [int] NOT NULL,
	[DocumentLineNumber] [nvarchar](5) NOT NULL,
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

