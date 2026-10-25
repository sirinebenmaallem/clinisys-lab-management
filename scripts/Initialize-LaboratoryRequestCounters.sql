-- Deployment only, after the approved migration, with application writers stopped.
-- Never run from the API or ordinary tests. Existing requests are never updated.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT IN (N'ClinisysDb',N'ClinisysTestDb')
    THROW 51030, 'Unexpected counter initialization target.', 1;
BEGIN TRANSACTION;
DECLARE @rowCount bigint;
SELECT @rowCount = COUNT_BIG(*) FROM dbo.LaboratoryRequests WITH (TABLOCKX,HOLDLOCK);
SELECT @rowCount = COUNT_BIG(*) FROM dbo.LaboratoryRequestCounters WITH (TABLOCKX,HOLDLOCK);
IF EXISTS (
    SELECT 1 FROM dbo.LaboratoryRequests
    WHERE UPPER(LEFT(Number,4)) = N'LAB-' AND (
        LEN(Number) <> 15 OR DATALENGTH(Number) <> 30 OR
        Number COLLATE Latin1_General_100_BIN2 NOT LIKE N'LAB-[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]' OR
        TRY_CONVERT(int,SUBSTRING(Number,5,4)) NOT BETWEEN 1 AND 9999 OR
        TRY_CONVERT(int,RIGHT(Number,6)) NOT BETWEEN 1 AND 999999 OR
        TRY_CONVERT(int,SUBSTRING(Number,5,4)) <> YEAR(SWITCHOFFSET(RegisteredAt,'+00:00'))))
    THROW 51031, 'Ambiguous historical request numbering. Manual review required.', 1;
DECLARE @history TABLE ([Year] int PRIMARY KEY, LastValue int NOT NULL);
INSERT @history
SELECT CONVERT(int,SUBSTRING(Number,5,4)), MAX(CONVERT(int,RIGHT(Number,6)))
FROM dbo.LaboratoryRequests WHERE LEFT(Number,4) = N'LAB-'
GROUP BY CONVERT(int,SUBSTRING(Number,5,4));
IF EXISTS (SELECT 1 FROM @history h JOIN dbo.LaboratoryRequestCounters c ON c.[Year]=h.[Year]
    WHERE c.LastValue < h.LastValue)
    THROW 51032, 'An existing counter is behind recorded requests. Manual review required.', 1;
INSERT dbo.LaboratoryRequestCounters ([Year],LastValue)
SELECT h.[Year],h.LastValue FROM @history h
WHERE NOT EXISTS (SELECT 1 FROM dbo.LaboratoryRequestCounters c WHERE c.[Year]=h.[Year]);
COMMIT TRANSACTION;
