#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange (mailto:Stefan.Lange@pdfsharp.com)
//
// Copyright (c) 2005-2019 empira Software GmbH, Cologne Area (Germany)
//
// http://www.pdfsharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections;
using PdfSharp.Drawing;
using PdfSharp.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Internal;
using System.Security.Cryptography;

namespace PdfSharp.Pdf.Security
{
    /// <summary>
    /// Represents the standard PDF security handler.
    /// </summary>
    public sealed class PdfStandardSecurityHandler : PdfSecurityHandler
    {
        // Object streams must be decrypted, when they were read but skipped, once the document is completely loaded
        private bool skipObjectStreams;

        internal PdfStandardSecurityHandler(PdfDocument document)
          : base(document)
        { }

        internal PdfStandardSecurityHandler(PdfDictionary dict)
          : base(dict)
        { }

        /// <summary>
        /// Sets the user password of the document. Setting a password automatically sets the
        /// PdfDocumentSecurityLevel to PdfDocumentSecurityLevel.Encrypted128Bit if its current
        /// value is PdfDocumentSecurityLevel.None.
        /// </summary>
        public string UserPassword
        {
            set
            {
                if (this._document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.None)
                    this._document._securitySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
                this._userPassword = value;
            }
        }
        internal string _userPassword;

        /// <summary>
        /// Sets the owner password of the document. Setting a password automatically sets the
        /// PdfDocumentSecurityLevel to PdfDocumentSecurityLevel.Encrypted128Bit if its current
        /// value is PdfDocumentSecurityLevel.None.
        /// </summary>
        public string OwnerPassword
        {
            set
            {
                if (this._document._securitySettings.DocumentSecurityLevel == PdfDocumentSecurityLevel.None)
                    this._document._securitySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
                this._ownerPassword = value;
            }
        }
        internal string _ownerPassword;

        /// <summary>
        /// Gets or sets the user access permission represented as an integer in the P key.
        /// </summary>
        internal PdfUserAccessPermission Permission
        {
            get
            {
                PdfUserAccessPermission permission = (PdfUserAccessPermission)Elements.GetInteger(Keys.P);
                if ((int)permission == 0)
                    permission = PdfUserAccessPermission.PermitAll;
                return permission;
            }
            set { Elements.SetInteger(Keys.P, (int)value); }
        }

        /// <summary>
        /// Encrypts the whole document.
        /// </summary>
        public void EncryptDocument()
        {
            skipObjectStreams = true;

            foreach (PdfReference iref in this._document._irefTable.AllReferences)
            {
                if (!ReferenceEquals(iref.Value, this))
                    EncryptObject(iref.Value);
            }
        }

        /// <summary>
        /// Encrypts an indirect object.
        /// </summary>
        internal void EncryptObject(PdfObject value)
        {
            Debug.Assert(value.Reference != null);

            stringEncryptor.CreateHashKey(value.ObjectID);

#if DEBUG
            if (value.ObjectID.ObjectNumber == 10)
                GetType();
#endif

            PdfDictionary dict;
            PdfArray array;
            PdfStringObject str;
            if ((dict = value as PdfDictionary) != null)
                EncryptDictionary(dict);
            else if ((array = value as PdfArray) != null)
                EncryptArray(array);
            else if ((str = value as PdfStringObject) != null)
            {
                if (str.Length != 0)
                {
                    byte[] bytes = str.EncryptionValue;
                    bytes = stringEncryptor.Encrypt(bytes);
                    str.EncryptionValue = bytes;
                }
            }
        }

        /// <summary>
        /// Encrypts a dictionary.
        /// </summary>
        void EncryptDictionary(PdfDictionary dict)
        {
            // Pdf Reference 1.7, Chapter 7.5.8.2: The cross-reference stream shall not be encrypted
            // Pdf Reference 1.7, Chapter 7.6.1: Strings in the Encryption-Dictionary shall not be encrypted
            if (dict.Elements.GetName("/Type") == "/XRef" || dict.ObjectNumber == ObjectNumber)
                return;
            if (skipObjectStreams && (dict.Elements.GetName("/Type") == "/ObjStm" || (dict.Reference != null && dict.Reference.Position < 0)))  // also skip objects read from object streams
                return;

            PdfName[] names = dict.Elements.KeyNames;
            foreach (KeyValuePair<string, PdfItem> item in dict.Elements)
            {
                PdfString value1;
                PdfDictionary value2;
                PdfArray value3;
                if ((value1 = item.Value as PdfString) != null)
                    EncryptString(value1);
                else if ((value2 = item.Value as PdfDictionary) != null)
                    EncryptDictionary(value2);
                else if ((value3 = item.Value as PdfArray) != null)
                    EncryptArray(value3);
            }
            if (dict.Stream != null)
            {
                byte[] bytes = dict.Stream.Value;
                if (bytes.Length != 0)
                {
                    streamEncryptor.CreateHashKey(dict.ObjectID);
                    bytes = streamEncryptor.Encrypt(bytes);
                    dict.Stream.Value = bytes;
                }
            }
        }

        /// <summary>
        /// Encrypts an array.
        /// </summary>
        void EncryptArray(PdfArray array)
        {
            int count = array.Elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                PdfItem item = array.Elements[idx];
                PdfString value1;
                PdfDictionary value2;
                PdfArray value3;
                if ((value1 = item as PdfString) != null)
                {
                    EncryptString(value1);
                }
                else if ((value2 = item as PdfDictionary) != null)
                    EncryptDictionary(value2);
                else if ((value3 = item as PdfArray) != null)
                    EncryptArray(value3);
            }
        }

        /// <summary>
        /// Encrypts a string.
        /// </summary>
        void EncryptString(PdfString value)
        {
            if (value.Length != 0)
            {
                byte[] bytes = value.EncryptionValue;
                bytes = stringEncryptor.Encrypt(bytes);
                value.EncryptionValue = bytes;
            }
        }

        /// <summary>
        /// Encrypts an array.
        /// </summary>
        internal byte[] EncryptBytes(byte[] bytes)
        {
            if (bytes != null && bytes.Length != 0)
            {
                // TODO: This may actually be a string-value (see PdfEncoders.FormatStringLiteral) and therefore, the stringEncryptor should be used
                // but as we currently only support one type for both strings and streams, it doesn't really matter...
                bytes = streamEncryptor.Encrypt(bytes);
            }
            return bytes;
        }

        #region Encryption Algorithms

        /// <summary>
        /// Checks the password.
        /// Entry point for decrypting a loaded document
        /// </summary>
        /// <param name="inputPassword">Password or null if no password is provided.</param>
        public PasswordValidity ValidatePassword(string inputPassword)
        {
            if (inputPassword == null)
                inputPassword = "";

            EncryptorFactory.InitDecryption(_document, this, out stringEncryptor, out streamEncryptor);
            if (stringEncryptor == null && streamEncryptor == null)
                throw new PdfSharpException("No suitable decryptor available. Send this document to support.");

            if (streamEncryptor == null && stringEncryptor != null)
                streamEncryptor = stringEncryptor;
            else if (stringEncryptor == null)
                stringEncryptor = streamEncryptor;

            stringEncryptor.InitEncryptionKey(inputPassword);
            streamEncryptor.InitEncryptionKey(inputPassword);

            stringEncryptor.ValidatePassword(inputPassword);

            if (stringEncryptor.PasswordValid && stringEncryptor.HaveOwnerPermission)
                return PasswordValidity.OwnerPassword;
            if (stringEncryptor.PasswordValid)
                return PasswordValidity.UserPassword;
            return PasswordValidity.Invalid;
        }

        /// <summary>
        /// Pads a password to a 32 byte array.
        /// </summary>
        static byte[] PadPassword(string password)
        {
            byte[] padded = new byte[32];
            if (password == null)
                Array.Copy(PasswordPadding, 0, padded, 0, 32);
            else
            {
                int length = password.Length;
                Array.Copy(PdfEncoders.RawEncoding.GetBytes(password), 0, padded, 0, Math.Min(length, 32));
                if (length < 32)
                    Array.Copy(PasswordPadding, 0, padded, length, 32 - length);
            }
            return padded;
        }
        static readonly byte[] PasswordPadding = // 32 bytes password padding defined by Adobe
            {
              0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
              0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
            };

        /// <summary>
        /// Generates the user key based on the padded user password.
        /// </summary>
        void InitWithUserPassword(byte[] documentID, string userPassword, byte[] ownerKey, int permissions, bool strongEncryption)
        {
            InitEncryptionKey(documentID, PadPassword(userPassword), ownerKey, permissions, strongEncryption);
            SetupUserKey(documentID);
        }

        /// <summary>
        /// Generates the user key based on the padded owner password.
        /// </summary>
        void InitWithOwnerPassword(byte[] documentID, string ownerPassword, byte[] ownerKey, int permissions, bool strongEncryption)
        {
            byte[] userPad = ComputeOwnerKey(ownerKey, PadPassword(ownerPassword), strongEncryption);
            InitEncryptionKey(documentID, userPad, ownerKey, permissions, strongEncryption);
            SetupUserKey(documentID);
        }

        /// <summary>
        /// Computes the padded user password from the padded owner password.
        /// </summary>
        byte[] ComputeOwnerKey(byte[] userPad, byte[] ownerPad, bool strongEncryption)
        {
            byte[] ownerKey = new byte[32];
            //#if !SILVERLIGHT
            byte[] digest = _md5.ComputeHash(ownerPad);
            if (strongEncryption)
            {
                byte[] mkey = new byte[16];
                // Hash the pad 50 times
                for (int idx = 0; idx < 50; idx++)
                    digest = _md5.ComputeHash(digest);
                Array.Copy(userPad, 0, ownerKey, 0, 32);
                // Encrypt the key
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < mkey.Length; ++j)
                        mkey[j] = (byte)(digest[j] ^ i);
                    PrepareRC4Key(mkey);
                    EncryptRC4(ownerKey);
                }
            }
            else
            {
                PrepareRC4Key(digest, 0, 5);
                EncryptRC4(userPad, ownerKey);
            }
            //#endif
            return ownerKey;
        }

        /// <summary>
        /// Computes the encryption key.
        /// </summary>
        void InitEncryptionKey(byte[] documentID, byte[] userPad, byte[] ownerKey, int permissions, bool strongEncryption)
        {
            //#if !SILVERLIGHT
            _ownerKey = ownerKey;
            _encryptionKey = new byte[strongEncryption ? 16 : 5];

#if !NETFX_CORE && !DNC10
            _md5.Initialize();
            _md5.TransformBlock(userPad, 0, userPad.Length, userPad, 0);
            _md5.TransformBlock(ownerKey, 0, ownerKey.Length, ownerKey, 0);

            // Split permission into 4 bytes
            byte[] permission = new byte[4];
            permission[0] = (byte)permissions;
            permission[1] = (byte)(permissions >> 8);
            permission[2] = (byte)(permissions >> 16);
            permission[3] = (byte)(permissions >> 24);
            _md5.TransformBlock(permission, 0, 4, permission, 0);
            _md5.TransformBlock(documentID, 0, documentID.Length, documentID, 0);
            _md5.TransformFinalBlock(permission, 0, 0);
            byte[] digest = _md5.Hash;
            _md5.Initialize();
            // Create the hash 50 times (only for 128 bit)
            if (_encryptionKey.Length == 16)
            {
                for (int idx = 0; idx < 50; idx++)
                {
                    digest = _md5.ComputeHash(digest);
                    _md5.Initialize();
                }
            }
            Array.Copy(digest, 0, _encryptionKey, 0, _encryptionKey.Length);
            //#endif
#endif
        }

        /// <summary>
        /// Computes the user key.
        /// </summary>
        void SetupUserKey(byte[] documentID)
        {
#if !NETFX_CORE && !DNC10
            //#if !SILVERLIGHT
            if (_encryptionKey.Length == 16)
            {
                _md5.TransformBlock(PasswordPadding, 0, PasswordPadding.Length, PasswordPadding, 0);
                _md5.TransformFinalBlock(documentID, 0, documentID.Length);
                byte[] digest = _md5.Hash;
                _md5.Initialize();
                Array.Copy(digest, 0, _userKey, 0, 16);
                for (int idx = 16; idx < 32; idx++)
                    _userKey[idx] = 0;
                //Encrypt the key
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < _encryptionKey.Length; j++)
                        digest[j] = (byte)(_encryptionKey[j] ^ i);
                    PrepareRC4Key(digest, 0, _encryptionKey.Length);
                    EncryptRC4(_userKey, 0, 16);
                }
            }
            else
            {
                PrepareRC4Key(_encryptionKey);
                EncryptRC4(PasswordPadding, _userKey);
            }
            //#endif
#endif
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareKey()
        {
            if (_key != null && _keySize > 0) //!!!mod 2017-11-06 Added "if" because PrepareRC4Key fails if _key is null. But _key appears to be always null, so maybe PrepareKey() is obsolete.
            PrepareRC4Key(_key, 0, _keySize);
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareRC4Key(byte[] key)
        {
            PrepareRC4Key(key, 0, key.Length);
        }

        /// <summary>
        /// Prepare the encryption key.
        /// </summary>
        void PrepareRC4Key(byte[] key, int offset, int length)
        {
            int idx1 = 0;
            int idx2 = 0;
            for (int idx = 0; idx < 256; idx++)
                _state[idx] = (byte)idx;
            byte tmp;
            for (int idx = 0; idx < 256; idx++)
            {
                idx2 = (key[idx1 + offset] + _state[idx] + idx2) & 255;
                tmp = _state[idx];
                _state[idx] = _state[idx2];
                _state[idx2] = tmp;
                idx1 = (idx1 + 1) % length;
            }
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        // ReSharper disable InconsistentNaming
        void EncryptRC4(byte[] data)
        // ReSharper restore InconsistentNaming
        {
            EncryptRC4(data, 0, data.Length, data);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        // ReSharper disable InconsistentNaming
        void EncryptRC4(byte[] data, int offset, int length)
        // ReSharper restore InconsistentNaming
        {
            EncryptRC4(data, offset, length, data);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        void EncryptRC4(byte[] inputData, byte[] outputData)
        {
            EncryptRC4(inputData, 0, inputData.Length, outputData);
        }

        /// <summary>
        /// Encrypts the data.
        /// </summary>
        void EncryptRC4(byte[] inputData, int offset, int length, byte[] outputData)
        {
            length += offset;
            int x = 0, y = 0;
            byte b;
            for (int idx = offset; idx < length; idx++)
            {
                x = (x + 1) & 255;
                y = (_state[x] + y) & 255;
                b = _state[x];
                _state[x] = _state[y];
                _state[y] = b;
                outputData[idx] = (byte)(inputData[idx] ^ _state[(_state[x] + _state[y]) & 255]);
            }
        }

        /// <summary>
        /// Checks whether the calculated key correct.
        /// </summary>
        bool EqualsKey(byte[] value, int length)
        {
            for (int idx = 0; idx < length; idx++)
            {
                if (_userKey[idx] != value[idx])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Set the hash key for the specified object.
        /// </summary>
        internal void SetHashKey(PdfObjectID id)
        {
#if !NETFX_CORE && !DNC10
            //#if !SILVERLIGHT
            byte[] objectId = new byte[5];
            _md5.Initialize();
            // Split the object number and generation
            objectId[0] = (byte)id.ObjectNumber;
            objectId[1] = (byte)(id.ObjectNumber >> 8);
            objectId[2] = (byte)(id.ObjectNumber >> 16);
            objectId[3] = (byte)id.GenerationNumber;
            objectId[4] = (byte)(id.GenerationNumber >> 8);
            _md5.TransformBlock(_encryptionKey, 0, _encryptionKey.Length, _encryptionKey, 0);
            _md5.TransformFinalBlock(objectId, 0, objectId.Length);
            _key = _md5.Hash;
            _md5.Initialize();
            _keySize = _encryptionKey.Length + 5;
            if (_keySize > 16)
                _keySize = 16;
            //#endif
#endif
            // stringEncryptor.CreateHashKey(id);
            // streamEncryptor.CreateHashKey(id);
        }

        /// <summary>
        /// Prepares the security handler for encrypting the document.
        /// Entry point for encryption of a document that is about to be saved.
        /// </summary>
        public void PrepareEncryption()
        {
#if !SILVERLIGHT
            Debug.Assert(this._document._securitySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None);
            int permissions = (int)this.Permission;
            bool strongEncryption = this._document._securitySettings.DocumentSecurityLevel >= PdfDocumentSecurityLevel.Encrypted128Bit;

            stringEncryptor = EncryptorFactory.InitEncryption(_document, this);
            streamEncryptor = stringEncryptor;

            // using the AES encryption implies a Pdf Version 1.7 ?
            //if (_document._securitySettings.DocumentSecurityLevel >= PdfDocumentSecurityLevel.EncryptedAES256 && _document.Version < 17)
            //    _document.Version = 17;

            // Correct permission bits
            permissions |= (int)(strongEncryption ? (uint)0xfffff0c0 : (uint)0xffffffc0);
            permissions &= unchecked((int)0xfffffffc);

            PdfInteger pValue = new PdfInteger(permissions);
            Elements[Keys.P] = pValue;
#endif
        }

        private IEncryptor stringEncryptor;

        private IEncryptor streamEncryptor;

        #endregion

        internal override void WriteObject(PdfWriter writer)
        {
            // Don't encypt myself
            PdfStandardSecurityHandler securityHandler = writer.SecurityHandler;
            writer.SecurityHandler = null;
            base.WriteObject(writer);
            writer.SecurityHandler = securityHandler;
        }

        #region Keys
        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed new class Keys : PdfSecurityHandler.Keys
        {
            /// <summary>
            /// (Required) A number specifying which revision of the standard security handler
            /// should be used to interpret this dictionary:
            /// � 2 if the document is encrypted with a V value less than 2 and does not have any of
            ///   the access permissions set (by means of the P entry, below) that are designated 
            ///   "Revision 3 or greater".
            /// � 3 if the document is encrypted with a V value of 2 or 3, or has any "Revision 3 or 
            ///   greater" access permissions set.
            /// � 4 if the document is encrypted with a V value of 4
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string R = "/R";

            /// <summary>
            /// (Required) A 32-byte string, based on both the owner and user passwords, that is
            /// used in computing the encryption key and in determining whether a valid owner
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string O = "/O";

            /// <summary>
            /// (Required) A 32-byte string, based on the user password, that is used in determining
            /// whether to prompt the user for a password and, if so, whether a valid user or owner 
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string U = "/U";

            /// <summary>
            /// (Required) A set of flags specifying which operations are permitted when the document
            /// is opened with user access.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string P = "/P";

            /// <summary>
            /// (ExtensionLevel 3; required if R is 5)
            /// A 32-byte string, based on the owner and user passwords, that is used in computing the encryption key.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string OE = "/OE";

            /// <summary>
            /// (ExtensionLevel 3; required if R is 5)
            /// A 32-byte string, based on the user password, that is used in computing the encryption key.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string UE = "/UE";

            /// <summary>
            /// (ExtensionLevel 3; required if R is 5)
            /// A 16-byte string, encrypted with the file encryption key, that contains an encrypted copy of the permission flags.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string Perms = "/Perms";

            /// <summary>
            /// (Optional; meaningful only when the value of V is 4; PDF 1.5) Indicates whether
            /// the document-level metadata stream is to be encrypted. Applications should respect this value.
            /// Default value: true.
            /// </summary>
            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string EncryptMetadata = "/EncryptMetadata";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static DictionaryMeta Meta
            {
                get
                {
                    if (meta == null)
                        meta = CreateMeta(typeof(Keys));
                    return meta;
                }
            }
            static DictionaryMeta meta;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
        #endregion
    }
}
