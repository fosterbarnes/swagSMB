using System;
using System.Diagnostics;
using System.Security.Cryptography;
using SMBLibrary;
using SMBLibrary.Authentication.NTLM;

namespace swagSMB.Core
{
    internal sealed class SecureNtlmAuthenticationProvider : IndependentNTLMAuthenticationProvider
    {
        public SecureNtlmAuthenticationProvider(GetUserPassword getUserPassword) : base(getUserPassword)
        {
        }

        public override NTStatus GetChallengeMessage(out object context, byte[] negotiateMessageBytes, out byte[] challengeMessageBytes)
        {
            NegotiateMessage negotiateMessage;
            try
            {
                negotiateMessage = new NegotiateMessage(negotiateMessageBytes);
            }
            catch
            {
                context = null;
                challengeMessageBytes = null;
                return NTStatus.SEC_E_INVALID_TOKEN;
            }

            byte[] serverChallenge = new byte[8];
            RandomNumberGenerator.Fill(serverChallenge);

            var authContext = new IndependentNTLMAuthenticationProvider.AuthContext(serverChallenge);
            context = authContext;

            ChallengeMessage challengeMessage = CreateChallengeMessage(negotiateMessage, serverChallenge);
            challengeMessageBytes = challengeMessage.GetBytes();
            return NTStatus.SEC_I_CONTINUE_NEEDED;
        }

        public override NTStatus Authenticate(object context, byte[] authenticateMessageBytes)
        {
            if (!IsNtlmV2(authenticateMessageBytes))
            {
                Debug.WriteLine("[SecureNtlm] Rejected non-NTLMv2 authentication attempt.");
                return NTStatus.STATUS_LOGON_FAILURE;
            }

            return base.Authenticate(context, authenticateMessageBytes);
        }

        private static bool IsNtlmV2(byte[] authenticateMessageBytes)
        {
            try
            {
                var message = new AuthenticateMessage(authenticateMessageBytes);
                return message.NtChallengeResponse != null
                    && AuthenticationMessageUtils.IsNTLMv2NTResponse(message.NtChallengeResponse);
            }
            catch
            {
                return false;
            }
        }
    }
}
