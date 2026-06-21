-- READ-ONLY audit query. Lists Penalty-type LedgerTransactions (weekly-incomplete docks from
-- WeeklyReconciliationService.cs ~line 235) joined to the ChoreLog rows from that same week,
-- flagged by suspicious Version spikes (a clean single completion has Version=1; repeated
-- undo/redo toggling - the bug this session fixed - inflates it). This is a candidate list for
-- manual review only. It does not modify any row. Adjust the HAVING threshold as needed.
SELECT
    lt."Id"                       AS penalty_transaction_id,
    lt."UserId",
    cd."Name"                     AS chore_name,
    lt."WeekEndDate",
    lt."Amount"                   AS penalty_amount,
    lt."Description",
    lt."CreatedAt"                AS penalty_created_at,
    COUNT(cl."Id")                AS chorelog_rows_that_week,
    MAX(cl."Version")             AS max_version_that_week,
    SUM(cl."Version")             AS sum_version_that_week
FROM "LedgerTransactions" lt
JOIN "ChoreDefinitions" cd ON cd."Id" = lt."ChoreDefinitionId"
LEFT JOIN "ChoreLogs" cl
    ON cl."ChoreDefinitionId" = lt."ChoreDefinitionId"
    AND cl."Date" BETWEEN lt."WeekEndDate" - INTERVAL '6 days' AND lt."WeekEndDate"
WHERE lt."Type" = 3 -- Penalty
GROUP BY lt."Id", lt."UserId", cd."Name", lt."WeekEndDate", lt."Amount", lt."Description", lt."CreatedAt"
HAVING MAX(cl."Version") >= 3
ORDER BY MAX(cl."Version") DESC, lt."WeekEndDate" DESC;
