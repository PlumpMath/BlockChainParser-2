﻿using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
using System.Linq;

namespace BlockChain 
{

    public static class Helper {
        private static readonly string[] m_hexEncodeValues; // byte -> hex, to massively speed up byte.ToString("x2")
        private static readonly byte[] m_hexDecodeValues;   // char -> 4 bits
        private static readonly bool[] m_validTextCharacters;

        static Helper() {
            m_hexEncodeValues = new string[256];
            for(int i = 0; i < 256; i++)
                m_hexEncodeValues[i] = i.ToString("x2", CultureInfo.InvariantCulture);
            m_hexDecodeValues = new byte[65536];
            int value = 0;
            foreach(var item in "0123456789abcdef")
                m_hexDecodeValues[item] = (byte)value++;
            value = 10;
            foreach(var item in "ABCDEF")
                m_hexDecodeValues[item] = (byte)value++;
            m_validTextCharacters = new bool[65536];
            foreach(var item in "\r\n\t")
                m_validTextCharacters[item] = true;
            for(int i = 32; i < 127; i++)
                m_validTextCharacters[i] = true;
            for(int i = 128; i < 169; i++)
                m_validTextCharacters[i] = true;
        }

        public static Hash SHA256(byte[] buffer, int offset, int count) {
            var sha256 = SHA256Managed.Create();
            var hash = sha256.ComputeHash(buffer, offset, count);
            return new Hash(hash);
        }
        /// <summary>
        ///     SHA256 ran twice.
        /// </summary>
        public static Hash SHA256_2(byte[] buffer, int offset, int count) {
            var sha256 = SHA256Managed.Create();
            var hash = sha256.ComputeHash(buffer, offset, count);
            hash = sha256.ComputeHash(hash);
            return new Hash(hash);
        }

        public static DateTime ReadUnixTimestamp(byte[] buffer, ref int index) {
            return ReadUnixTimestamp(
                ((uint)buffer[index++] << 0)  |
                ((uint)buffer[index++] << 8)  |
                ((uint)buffer[index++] << 16) |
                ((uint)buffer[index++] << 24));
        }

        public static DateTime ReadUnixTimestamp(uint value) {
            // DateTimeOffset.FromUnixTimeSeconds() 4.6+ only
            return DateTime.SpecifyKind(new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(value), DateTimeKind.Utc);
        }

        public static uint ToUnixTimestamp(DateTime value) {
            return (uint)(value - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static ulong ReadVariableUInt(byte[] buffer, ref int index) {
            byte first = buffer[index++];
            if(first < 0xFD)
                return first;
            if(first == 0xFD)
                return 
                    ((uint)buffer[index++] << 0) |
                    ((uint)buffer[index++] << 8);
            if(first == 0xFE)
                return
                    ((uint)buffer[index++] << 0)  |
                    ((uint)buffer[index++] << 8)  |
                    ((uint)buffer[index++] << 16) |
                    ((uint)buffer[index++] << 24);
            return
                ((ulong)buffer[index++] << 0)  |
                ((ulong)buffer[index++] << 8)  |
                ((ulong)buffer[index++] << 16) |
                ((ulong)buffer[index++] << 24) |
                ((ulong)buffer[index++] << 32) |
                ((ulong)buffer[index++] << 40) |
                ((ulong)buffer[index++] << 48) |
                ((ulong)buffer[index++] << 56);
        }
        public static void WriteVariableUInt(ulong value, byte[] buffer, ref int index) {
            if(value < 0xFD)
                buffer[index++] = (byte)value;
            else if(value <= 0xFFFF) {
                buffer[index++] = (byte)0xFD;
                buffer[index++] = (byte)((value >> 0)  & 0xFF);
                buffer[index++] = (byte)((value >> 8)  & 0xFF);
            } else if(value <= 0xFFFFFFFF) {
                buffer[index++] = (byte)0xFE;
                buffer[index++] = (byte)((value >> 0)  & 0xFF);
                buffer[index++] = (byte)((value >> 8)  & 0xFF);
                buffer[index++] = (byte)((value >> 16) & 0xFF);
                buffer[index++] = (byte)((value >> 24) & 0xFF);
            } else {
                buffer[index++] = (byte)0xFF;
                buffer[index++] = (byte)((value >> 0)  & 0xFF);
                buffer[index++] = (byte)((value >> 8)  & 0xFF);
                buffer[index++] = (byte)((value >> 16) & 0xFF);
                buffer[index++] = (byte)((value >> 24) & 0xFF);
                buffer[index++] = (byte)((value >> 32) & 0xFF);
                buffer[index++] = (byte)((value >> 40) & 0xFF);
                buffer[index++] = (byte)((value >> 48) & 0xFF);
                buffer[index++] = (byte)((value >> 56) & 0xFF);
            }
        }

        public static string HexEncode(byte[] value) {
            //return string.Join(string.Empty, value.Select(o => o.ToString("x2", CultureInfo.InvariantCulture)));
            var sb = new StringBuilder(value.Length << 1);
            for(int i = 0; i < value.Length; i++)
                sb.Append(m_hexEncodeValues[value[i]]);
            return sb.ToString();
        }
        public static byte[] HexDecode(string value) {
            if(value.Length == 0)
                return new byte[0];
            // we shouldnt have an odd number of characters, but if we do, the only
            // sensible way to interpret the data is that the extra 4 bits have to be the least significant bits
            // as such they only make sense at the beginning of the string
            var is_even = (value.Length & 0x01) == 0;
            var res = new byte[(value.Length >> 1) + (is_even ? 0 : 1)];
            int index = 0;
            if(!is_even)
                res[index++] = m_hexDecodeValues[value[0]];
            for(int i = (is_even ? 0 : 1); i < value.Length; i += 2) {
                res[index++] = (byte)
                    ((m_hexDecodeValues[value[i + 0]] << 4) |
                     (m_hexDecodeValues[value[i + 1]] << 0));
            }
            return res;
        }
        public static string HexDecode(string value, Encoding encoding) {
            return encoding.GetString(HexDecode(value));
        }

        private static Regex m_scriptPattern = new Regex(@"\b[0-9a-fA-F]{2,}\b", RegexOptions.Compiled);
        public static string DetectScriptPattern(string script) {
            return m_scriptPattern.Replace(script, m => $"[hex size={m.Length.ToString(CultureInfo.InvariantCulture)}]").Trim();
        }
        public static string ExtractScriptChunks(string script) {
            var sb = new StringBuilder(script.Length);
            foreach(Match m in m_scriptPattern.Matches(script))
                sb.Append(m.Value);
            return sb.ToString();
        }

        #region public static ExtractText()
        public static string ExtractText(byte[] buffer) {
            const int MIN_TEXT_SIZE = 4;
            const string CHUNK_SEPARATOR = " / ";

            if(buffer == null || buffer.Length < MIN_TEXT_SIZE)
                return null;
            var value = Encoding.UTF8.GetString(buffer);

            int chunk_start = 0;
            var sb = new StringBuilder(value.Length);
            for(int i = 0; i < value.Length; i++) {
                var valid_character = m_validTextCharacters[value[i]];
                if(!valid_character || i == value.Length - 1) {
                    var length = i - chunk_start + (!valid_character ? 0 : 1);
                    if(length >= MIN_TEXT_SIZE) {
                        var valid = FilterUnlikelyTextPatterns(value, chunk_start, length);

                        if(valid) {
                            if(sb.Length > 0)
                                sb.Append(CHUNK_SEPARATOR);
                            sb.Append(value, chunk_start, length);
                        }
                    }
                    chunk_start = i + 1;
                }
            }
            
            return sb.Length == 0 ? null : sb.ToString();
        }
        #endregion

        private static Regex m_unlikelyTextPatternRegex = new Regex("[a-z][A-Z]|[0-9][a-zA-Z]|[^a-zA-Z0-9]@|@[^a-zA-Z0-9]", RegexOptions.Compiled);
        private static bool FilterUnlikelyTextPatterns(string value, int index, int length) {
            int[] MIN_ALPHANUM_CHARACTERS_NEEDED_PER_LENGTH = new[] { 1, 2, 3, 4, 5, 5, 6, 6 };

            // more complex sanity checks on small chunks
            if(length <= 8) {
                int count = 0;
                for(int i = 0; i < length; i++) {
                    var c = value[index + i];
                    if((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                        count++;
                }
                if(count < MIN_ALPHANUM_CHARACTERS_NEEDED_PER_LENGTH[length - 1])
                    return false;

                var m = m_unlikelyTextPatternRegex.Match(value, index, length);
                return m == null || !m.Success;
            }

            return true;
        }
    }
}
