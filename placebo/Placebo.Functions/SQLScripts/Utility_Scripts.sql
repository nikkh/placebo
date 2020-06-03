DELETE FROM [dbo].[Invoice]
DELETE FROM [dbo].[InvoiceLineItem]
DELETE FROM [dbo].[InvoiceError]
delete from modeltraining


DROP TABLE [dbo].[Invoice]
DROP TABLE [dbo].[InvoiceLineItem]
DROP TABLE [dbo].[InvoiceError]
drop table ModelTraining

ALTER TABLE Invoice
ADD ModelVersion [nvarchar](50) NULL
 
SELECT * FROM [dbo].[Invoice] 
SELECT * FROM [dbo].[InvoiceLineItem] 
SELECT * FROM [dbo].[InvoiceError] 
select * from modeltraining


SELECT DocumentFormat, ModelId, ModelVersion, UpdatedDateTime, AverageModelAccuracy 
FROM ModelTraining 
WHERE ModelVersion=(SELECT max(ModelVersion) FROM ModelTraining WHERE DocumentFormat='alliance') AND DocumentFormat='alliance'

delete from ModelTraining where DocumentFormat='deyorkshire'
