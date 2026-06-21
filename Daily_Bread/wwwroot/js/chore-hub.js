(function () {
    "use strict";

    let connection = null;
    let dotNetReference = null;
    let retryTimer = null;
    let stopping = false;

    function registerHandlers() {
        connection.on("DashboardChanged", (affectedUserIds, timestamp) =>
            dotNetReference.invokeMethodAsync("OnDashboardChanged", affectedUserIds, timestamp));
        connection.on("HelpAlert", (choreLogId, requestingUserId, choreName, childName, timestamp) =>
            dotNetReference.invokeMethodAsync(
                "OnHelpAlert",
                choreLogId,
                requestingUserId,
                choreName,
                childName,
                timestamp));
        connection.on("BlessingGranted", (childUserId, choreName, earnedAmount, parentName, timestamp) =>
            dotNetReference.invokeMethodAsync(
                "OnBlessingGranted",
                childUserId,
                choreName,
                earnedAmount,
                parentName,
                timestamp));
        connection.on("HelpResponded", (childUserId, choreName, response, parentName, note, timestamp) =>
            dotNetReference.invokeMethodAsync(
                "OnHelpResponded",
                childUserId,
                choreName,
                response,
                parentName,
                note,
                timestamp));
        connection.on("ChoreUndone", (childUserId, choreName, parentName, timestamp) =>
            dotNetReference.invokeMethodAsync(
                "OnChoreUndone",
                childUserId,
                choreName,
                parentName,
                timestamp));

        connection.onreconnected(() => {
            if (dotNetReference) {
                return dotNetReference.invokeMethodAsync("OnSignalRReconnected");
            }
        });
        connection.onclose(() => {
            if (!stopping) {
                scheduleRetry();
            }
        });
    }

    async function startConnection() {
        if (!connection || stopping || connection.state !== signalR.HubConnectionState.Disconnected) {
            return;
        }

        try {
            await connection.start();
            console.info("Chore notifications connected.");
        } catch (error) {
            console.warn("Chore notifications could not connect; retrying.", error);
            scheduleRetry();
        }
    }

    function scheduleRetry() {
        if (retryTimer || stopping) {
            return;
        }

        retryTimer = window.setTimeout(async () => {
            retryTimer = null;
            await startConnection();
        }, 5000);
    }

    window.dailyBreadChoreHub = {
        start: async function (reference) {
            await this.stop();

            stopping = false;
            dotNetReference = reference;
            connection = new signalR.HubConnectionBuilder()
                .withUrl("/chorehub")
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 60000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            registerHandlers();
            await startConnection();
            return connection.state === signalR.HubConnectionState.Connected;
        },

        stop: async function () {
            stopping = true;
            if (retryTimer) {
                window.clearTimeout(retryTimer);
                retryTimer = null;
            }

            if (connection) {
                const connectionToStop = connection;
                connection = null;
                await connectionToStop.stop();
            }
            dotNetReference = null;
        }
    };

})();
