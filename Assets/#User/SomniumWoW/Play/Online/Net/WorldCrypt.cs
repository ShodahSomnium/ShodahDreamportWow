using System.Security.Cryptography;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>RC4 stream cipher (the OpenSSL "RC4" AzerothCore's ARC4.cpp uses).</summary>
    public sealed class Arc4
    {
        readonly byte[] _s = new byte[256];
        int _i, _j;

        public Arc4(byte[] key)
        {
            for (int i = 0; i < 256; i++) _s[i] = (byte)i;
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + _s[i] + key[i % key.Length]) & 0xFF;
                byte t = _s[i]; _s[i] = _s[j]; _s[j] = t;
            }
        }

        public void Process(byte[] data, int offset, int count)
        {
            for (int k = 0; k < count; k++)
            {
                _i = (_i + 1) & 0xFF;
                _j = (_j + _s[_i]) & 0xFF;
                byte t = _s[_i]; _s[_i] = _s[_j]; _s[_j] = t;
                data[offset + k] ^= _s[(_s[_i] + _s[_j]) & 0xFF];
            }
        }
    }

    /// <summary>
    /// World header encryption, AzerothCore AuthCrypt.cpp:
    ///   _serverEncrypt.Init(HMAC_SHA1(ServerEncryptionKey CC 98 AE 04 E8 97 EA CA 12 DD C0 93 42 91 53 57, K))
    ///   _clientDecrypt.Init(HMAC_SHA1(ServerDecryptionKey C2 B3 72 3C C6 AE D9 B5 34 3C 53 EE 2F 43 67 CE, K))
    ///   "Drop first 1024 bytes, as WoW uses ARC4-drop1024."
    /// Only headers are encrypted (server: 4 or 5 bytes, client: 6 bytes). The client decrypts what the server encrypted
    /// (ServerEncryptionKey) and encrypts with the key the server decrypts with (ServerDecryptionKey). forServer = the
    /// harness's fake server side.
    /// </summary>
    public sealed class WorldCrypt
    {
        public static readonly byte[] ServerEncryptionKey = { 0xCC, 0x98, 0xAE, 0x04, 0xE8, 0x97, 0xEA, 0xCA, 0x12, 0xDD, 0xC0, 0x93, 0x42, 0x91, 0x53, 0x57 };
        public static readonly byte[] ServerDecryptionKey = { 0xC2, 0xB3, 0x72, 0x3C, 0xC6, 0xAE, 0xD9, 0xB5, 0x34, 0x3C, 0x53, 0xEE, 0x2F, 0x43, 0x67, 0xCE };

        readonly Arc4 _send, _recv;

        public WorldCrypt(byte[] sessionKey, bool forServer)
        {
            var toClient = new Arc4(Hmac(ServerEncryptionKey, sessionKey));
            var toServer = new Arc4(Hmac(ServerDecryptionKey, sessionKey));
            var drop = new byte[1024];
            toClient.Process(drop, 0, 1024);
            drop = new byte[1024];
            toServer.Process(drop, 0, 1024);
            _send = forServer ? toClient : toServer;
            _recv = forServer ? toServer : toClient;
        }

        static byte[] Hmac(byte[] key, byte[] data)
        {
            using (var h = new HMACSHA1(key)) return h.ComputeHash(data);
        }

        public void Encrypt(byte[] data, int offset, int count) { _send.Process(data, offset, count); }
        public void Decrypt(byte[] data, int offset, int count) { _recv.Process(data, offset, count); }
    }
}
