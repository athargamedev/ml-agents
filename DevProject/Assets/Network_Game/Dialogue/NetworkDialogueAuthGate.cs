using System;

namespace Network_Game.Dialogue
{
    internal static class NetworkDialogueAuthGate
    {
        public static bool CanAccept(
            bool requireAuthenticatedPlayers,
            bool isUserInitiated,
            ulong requestingClientId,
            Func<ulong, bool> hasIdentitySnapshot,
            out string rejectionReason
        )
        {
            rejectionReason = null;

            if (!requireAuthenticatedPlayers)
            {
                return true;
            }

            if (!isUserInitiated)
            {
                return true;
            }

            // Netcode host uses clientId 0 for its local client.
            // Keep strictness for true clients by letting the caller pass a host-aware predicate.
            if (requestingClientId == 0)
            {
                if (hasIdentitySnapshot == null || !hasIdentitySnapshot(0))
                {
                    rejectionReason = "auth_missing_client";
                    return false;
                }

                return true;
            }

            if (hasIdentitySnapshot == null || !hasIdentitySnapshot(requestingClientId))
            {
                rejectionReason = "auth_missing_identity";
                return false;
            }

            return true;
        }
    }
}
