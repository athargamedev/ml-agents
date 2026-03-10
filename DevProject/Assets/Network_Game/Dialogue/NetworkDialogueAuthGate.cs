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

            if (requestingClientId == 0)
            {
                rejectionReason = "auth_missing_client";
                return false;
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
