//
// System.Security.Cryptography.RijndaelManaged.cs
//
// Authors: Mark Crichton (crichton@gimp.org)
//	    Andrew Birkett (andy@nobugs.org)
//          Sebastien Pouliot (spouliot@motus.com)
//
// (C) 2002
// Portions (C) 2002, 2003 Motus Technologies Inc. (http://www.motus.com)
//

using System;
using Mono.Security.Cryptography;

/// <summary>
/// Rijndael is a symmetric block cipher supporting block and key sizes
/// of 128, 192 and 256 bits.  It has been chosen as the AES cipher.
/// </summary>

namespace System.Security.Cryptography {
	
	// References:
	// a.	FIPS PUB 197: Advanced Encryption Standard
	//	http://csrc.nist.gov/publications/fips/fips197/fips-197.pdf
	
	public sealed class RijndaelManaged : Rijndael {
		
		/// <summary>
		/// RijndaelManaged constructor.
		/// </summary>
		public RijndaelManaged() {}
		
		/// <summary>
		/// Generates a random IV for block feedback modes
		/// </summary>
		/// <remarks>
		/// Method is inherited from SymmetricAlgorithm
		/// </remarks>
		public override void GenerateIV () 
		{
			IVValue = KeyBuilder.IV (BlockSizeValue >> 3);
		}
		
		/// <summary>
		/// Generates a random key for Rijndael.  Uses the current KeySize.
		/// </summary>
		/// <remarks>
		/// Inherited method from base class SymmetricAlgorithm
		/// </remarks>
		public override void GenerateKey () 
		{
			KeyValue = KeyBuilder.Key (KeySizeValue >> 3);
		}
		
		/// <summary>
		/// Creates a symmetric Rijndael decryptor object
		/// </summary>
		/// <remarks>
		/// Inherited method from base class SymmetricAlgorithm
		/// </remarks>
		/// <param name='rgbKey'>Key for Rijndael</param>
		/// <param name='rgbIV'>IV for chaining mode</param>
		public override ICryptoTransform CreateDecryptor (byte[] rgbKey, byte[] rgbIV) 
		{
			Key = rgbKey;
			IV = rgbIV;
			return new RijndaelTransform (this, false, rgbKey, rgbIV);
		}
		
		/// <summary>
		/// Creates a symmetric Rijndael encryptor object
		/// </summary>
		/// <remarks>
		/// Inherited method from base class SymmetricAlgorithm
		/// </remarks>
		/// <param name='rgbKey'>Key for Rijndael</param>
		/// <param name='rgbIV'>IV for chaining mode</param>
		public override ICryptoTransform CreateEncryptor (byte[] rgbKey, byte[] rgbIV) 
		{
			Key = rgbKey;
			IV = rgbIV;
			return new RijndaelTransform (this, true, rgbKey, rgbIV);
		}
	}
	
	
	internal class RijndaelTransform : SymmetricTransform
	{
		private byte[] key;
		private byte[] expandedKey;
	
		private int Nb;
		private int Nk;
		private int Nr;
		private byte[] eshifts;
		private byte[] dshifts;
	
		private byte[] state;
		private byte[] shifttemp;
	
		private static readonly byte[] e256shifts = { 1, 3, 4 };
		private static readonly byte[] d256shifts = { 7, 5, 4 };
		private static readonly byte[] e192shifts = { 1, 2, 3 };
		private static readonly byte[] d192shifts = { 5, 4, 3 };
		private static readonly byte[] e128shifts = { 1, 2, 3 };
		private static readonly byte[] d128shifts = { 3, 2, 1 };
	
		private static readonly Int32[] rcon = { 0, 16777216, 33554432, 67108864, 134217728, 268435456, 536870912, 1073741824, -2147483648, 452984832, 905969664, 1811939328, -671088640, -1426063360, 1291845632, -1711276032, 788529152, 1577058304, -1140850688, 1660944384, -973078528, -1761607680, 889192448, 1778384896, -738197504, -1291845632, 2097152000, -100663296, -285212672, -989855744, -1862270976 };
	
		public RijndaelTransform (Rijndael algo, bool encryption, byte[] key, byte[] iv) : base (algo, encryption, iv)
		{
			int keySize = algo.KeySize;
			if (keySize != 128 && keySize != 192 && keySize != 256) 
				throw new ArgumentException("Illegal key size");
	
			int blockSize = algo.BlockSize;
			if (blockSize != 128 && blockSize != 192 && blockSize != 256) 
				throw new ArgumentException("Illegal block size");
	
			if ((key.Length << 3) != keySize) 
				throw new ArgumentException("Key size doesn't match key");
	
			this.key = key;
			this.Nb = (blockSize >> 5); // div 32
			this.Nk = (keySize >> 5); // div 32
			state = new byte [Nb << 2];
			shifttemp = new byte [Nb << 2];
	
			if (Nb == 8 || Nk == 8) {
				Nr = 14;
			} else if (Nb == 6 || Nk == 6) {
				Nr = 12;
			} else {
				Nr = 10;
			}
	
			switch (Nb) {
				case 8: // 256 bits
					eshifts = e256shifts;
					dshifts = d256shifts;
					break;
				case 6: // 192 bits
					eshifts = e192shifts;
					dshifts = d192shifts;
					break;
				case 4: // 128 bits
					eshifts = e128shifts;
					dshifts = d128shifts;
					break;
			}
	
			// Setup Expanded Key
			int exKeySize = Nb * (Nr+1);
			Int32[] exKey = new Int32[exKeySize];
			int pos = 0;
			for (int i=0; i < Nk; i++) {
				Int32 value = (key [pos++] << 24);
				value |= (key [pos++] << 16);
				value |= (key [pos++] << 8);
				value |= (key [pos++]);
				exKey [i] = value;
			}
	
			for (int i = Nk; i < exKeySize; i++) {
				Int32 temp = exKey [i-1];
				if (i % Nk == 0) {
					Int32 rot = (Int32) ((temp << 8) | ((temp >> 24) & 0xff));
					temp = SubByte (rot) ^ rcon [i / Nk];
				} else if (Nk > 6 && (i % Nk) == 4) {
					temp = SubByte (temp);
				}
				exKey [i] = exKey [i-Nk] ^ temp;
			}
	
			// convert key to byte array (better performance)
			// TODO: Don't convert - expand key directly in byte array
			expandedKey = new byte [exKey.Length << 2];
			int k = 0;
			for (int i=0; i < exKey.Length; i++) {
				expandedKey [k++] = (byte) (exKey[i] >> 24);
				expandedKey [k++] = (byte) ((exKey[i] >> 16) & 0xff);
				expandedKey [k++] = (byte) ((exKey[i] >> 8) & 0xff);
				expandedKey [k++] = (byte) (exKey[i] & 0xff);
			}
		}
	
		// note: this method is guaranteed to be called with a valid blocksize
		// for both input and output
		protected override void ECB (byte[] input, byte[] output) 
		{
			int pos, ori;
			for (pos = 0; pos < input.Length; pos++)
				state [pos] = input [pos];
	
			if (encrypt) {
				// inline AddRoundKey (0, true);
				int roundoffset = 0;
				int p = 0;
				for (pos = 0; pos < state.Length; pos++)
					state [pos] ^= expandedKey [p++];
	
				for (int round = 1; round < Nr; round++) {
					// inline ByteSub (true);
					for (pos = 0; pos < state.Length; pos++)
						state [pos] = sbox [state [pos]];
	
					ShiftRow (true);
	
					// inline MixColumn ();
					pos = 0;
					ori = 0;
					for (int col = 0; col < Nb; col++) {
						byte s0 = state [pos++];
						byte s1 = state [pos++];
						byte s2 = state [pos++];
						byte s3 = state [pos++];
						state [ori++] = (byte) (mult2 [s0] ^ mult3 [s1] ^ s2 ^ s3);
						state [ori++] = (byte) (mult2 [s1] ^ mult3 [s2] ^ s3 ^ s0);
						state [ori++] = (byte) (mult2 [s2] ^ mult3 [s3] ^ s0 ^ s1);
						state [ori++] = (byte) (mult2 [s3] ^ mult3 [s0] ^ s1 ^ s2);
					}
	
					// inline AddRoundKey (round, true);
					roundoffset += Nb;
					p = (roundoffset << 2);
					for (pos = 0; pos < state.Length; pos++)
						state [pos] ^= expandedKey [p++];
				}
				// inline ByteSub (true);
				for (pos = 0; pos < state.Length; pos++)
					state [pos] = sbox [state [pos]];
	
				ShiftRow (true);
	
				// inline AddRoundKey (Nr, true);
				roundoffset += Nb;
				p = (roundoffset << 2);
				for (pos = 0; pos < state.Length; pos++)
					state [pos] ^= expandedKey [p++];
			}
			else {
				// inline AddRoundKey (0, false);
				int roundoffset = Nb * Nr;
				int p = (roundoffset << 2);
				for (pos = 0; pos < state.Length; pos++)
					state [pos] ^= expandedKey [p++];
	
	                        ShiftRow (false);
				// inline ByteSub (false);
				for (pos = 0; pos < state.Length; pos++)
					state [pos] = invSbox [state [pos]];
	
				for (int round = 1; round < Nr; round++) {
					// inline AddRoundKey (round, false);
					roundoffset -= Nb;
					p = (roundoffset << 2);
					for (pos = 0; pos < state.Length; pos++)
						state [pos] ^= expandedKey [p++];
	
					// inline InvMixColumn ();
					pos = 0;
					ori = 0;
					for (int col = 0; col < Nb; col++) {
						byte s0 = state [pos++];
						byte s1 = state [pos++];
						byte s2 = state [pos++];
						byte s3 = state [pos++];
						state [ori++] = (byte) (multE [s0] ^ multB [s1] ^ multD [s2] ^ mult9 [s3]);
						state [ori++] = (byte) (multE [s1] ^ multB [s2] ^ multD [s3] ^ mult9 [s0]);
						state [ori++] = (byte) (multE [s2] ^ multB [s3] ^ multD [s0] ^ mult9 [s1]);
						state [ori++] = (byte) (multE [s3] ^ multB [s0] ^ multD [s1] ^ mult9 [s2]);
					}
	
					ShiftRow (false);
	
					// inline ByteSub (false);
					for (pos = 0; pos < state.Length; pos++)
						state [pos] = invSbox [state [pos]];
				}
	
				// inline AddRoundKey (Nr, false);
				roundoffset -= Nb;
				p = (roundoffset << 2);
				for (pos = 0; pos < state.Length; pos++)
					state [pos] ^= expandedKey [p++];
			}
	
			for (pos = 0; pos < input.Length; pos++)
				output [pos] = state [pos];
		}
	
		private void ShiftRow (bool encrypt)
		{
			byte[] shifts = encrypt ? eshifts : dshifts;
			int pos = 0;
			for (int col = 0; col < Nb; col++) {
				shifttemp [pos] = state [pos++];
	
				int source_col = (col + shifts [0]);
				if (source_col >= Nb) source_col -= Nb;
				shifttemp [pos++] = state [(source_col << 2) + 1];
	
				source_col = (col + shifts [1]);
				if (source_col >= Nb) source_col -= Nb;
				shifttemp [pos++] = state [(source_col << 2) + 2];
	
				source_col = (col + shifts [2]);
				if (source_col >= Nb) source_col -= Nb;
				shifttemp [pos++] = state [(source_col << 2) + 3];
			}
	
			for (pos = 0; pos < state.Length; pos++)
				state [pos] = shifttemp [pos];
		}
	
		private Int32 SubByte (Int32 a)
		{
			Int32 value = 0xff & a;
			Int32 result = sbox [value]; 
			value = 0xff & (a >> 8);
			result |= sbox [value] << 8; 
			value = 0xff & (a >> 16);
			result |= sbox [value] << 16; 
			value = 0xff & (a >> 24);
			return result | (sbox [value] << 24);
		}
	
		// Constant tables used in the cipher
		static readonly byte[] sbox = {
		99, 124, 119, 123, 242, 107, 111, 197,  48,   1, 103,  43, 254, 215, 171, 118, 
		202, 130, 201, 125, 250,  89,  71, 240, 173, 212, 162, 175, 156, 164, 114, 192, 
		183, 253, 147,  38,  54,  63, 247, 204,  52, 165, 229, 241, 113, 216,  49,  21, 
		4, 199,  35, 195,  24, 150,   5, 154,   7,  18, 128, 226, 235,  39, 178, 117, 
		9, 131,  44,  26,  27, 110,  90, 160,  82,  59, 214, 179,  41, 227,  47, 132, 
		83, 209,   0, 237,  32, 252, 177,  91, 106, 203, 190,  57,  74,  76,  88, 207, 
		208, 239, 170, 251,  67,  77,  51, 133,  69, 249,   2, 127,  80,  60, 159, 168, 
		81, 163,  64, 143, 146, 157,  56, 245, 188, 182, 218,  33,  16, 255, 243, 210, 
		205,  12,  19, 236,  95, 151,  68,  23, 196, 167, 126,  61, 100,  93,  25, 115, 
		96, 129,  79, 220,  34,  42, 144, 136,  70, 238, 184,  20, 222,  94,  11, 219, 
		224,  50,  58,  10,  73,   6,  36,  92, 194, 211, 172,  98, 145, 149, 228, 121, 
		231, 200,  55, 109, 141, 213,  78, 169, 108,  86, 244, 234, 101, 122, 174,   8, 
		186, 120,  37,  46,  28, 166, 180, 198, 232, 221, 116,  31,  75, 189, 139, 138, 
		112,  62, 181, 102,  72,   3, 246,  14,  97,  53,  87, 185, 134, 193,  29, 158, 
		225, 248, 152,  17, 105, 217, 142, 148, 155,  30, 135, 233, 206,  85,  40, 223, 
		140, 161, 137,  13, 191, 230,  66, 104,  65, 153,  45,  15, 176,  84, 187,  22 
		};
	
		static readonly byte[] invSbox = {
		82,   9, 106, 213,  48,  54, 165,  56, 191,  64, 163, 158, 129, 243, 215, 251, 
		124, 227,  57, 130, 155,  47, 255, 135,  52, 142,  67,  68, 196, 222, 233, 203, 
		84, 123, 148,  50, 166, 194,  35,  61, 238,  76, 149,  11,  66, 250, 195,  78, 
		8,  46, 161, 102,  40, 217,  36, 178, 118,  91, 162,  73, 109, 139, 209,  37, 
		114, 248, 246, 100, 134, 104, 152,  22, 212, 164,  92, 204,  93, 101, 182, 146, 
		108, 112,  72,  80, 253, 237, 185, 218,  94,  21,  70,  87, 167, 141, 157, 132, 
		144, 216, 171,   0, 140, 188, 211,  10, 247, 228,  88,   5, 184, 179,  69,   6, 
		208,  44,  30, 143, 202,  63,  15,   2, 193, 175, 189,   3,   1,  19, 138, 107, 
		58, 145,  17,  65,  79, 103, 220, 234, 151, 242, 207, 206, 240, 180, 230, 115, 
		150, 172, 116,  34, 231, 173,  53, 133, 226, 249,  55, 232,  28, 117, 223, 110, 
		71, 241,  26, 113,  29,  41, 197, 137, 111, 183,  98,  14, 170,  24, 190,  27, 
		252,  86,  62,  75, 198, 210, 121,  32, 154, 219, 192, 254, 120, 205,  90, 244, 
		31, 221, 168,  51, 136,   7, 199,  49, 177,  18,  16,  89,  39, 128, 236,  95, 
		96,  81, 127, 169,  25, 181,  74,  13,  45, 229, 122, 159, 147, 201, 156, 239, 
		160, 224,  59,  77, 174,  42, 245, 176, 200, 235, 187,  60, 131,  83, 153,  97, 
		23,  43,   4, 126, 186, 119, 214,  38, 225, 105,  20,  99,  85,  33,  12, 125
		};
	
	/* Original tables - DO NOT DELETE !
	 * 	static byte[] logtable = {
		0,   0,  25,   1,  50,   2,  26, 198,  75, 199,  27, 104,  51, 238, 223,   3, 
		100,   4, 224,  14,  52, 141, 129, 239,  76, 113,   8, 200, 248, 105,  28, 193, 
		125, 194,  29, 181, 249, 185,  39, 106,  77, 228, 166, 114, 154, 201,   9, 120, 
		101,  47, 138,   5,  33,  15, 225,  36,  18, 240, 130,  69,  53, 147, 218, 142, 
		150, 143, 219, 189,  54, 208, 206, 148,  19,  92, 210, 241,  64,  70, 131,  56, 
		102, 221, 253,  48, 191,   6, 139,  98, 179,  37, 226, 152,  34, 136, 145,  16, 
		126, 110,  72, 195, 163, 182,  30,  66,  58, 107,  40,  84, 250, 133,  61, 186, 
		43, 121,  10,  21, 155, 159,  94, 202,  78, 212, 172, 229, 243, 115, 167,  87, 
		175,  88, 168,  80, 244, 234, 214, 116,  79, 174, 233, 213, 231, 230, 173, 232, 
		44, 215, 117, 122, 235,  22,  11, 245,  89, 203,  95, 176, 156, 169,  81, 160, 
		127,  12, 246, 111,  23, 196,  73, 236, 216,  67,  31,  45, 164, 118, 123, 183, 
		204, 187,  62,  90, 251,  96, 177, 134,  59,  82, 161, 108, 170,  85,  41, 157, 
		151, 178, 135, 144,  97, 190, 220, 252, 188, 149, 207, 205,  55,  63,  91, 209, 
		83,  57, 132,  60,  65, 162, 109,  71,  20,  42, 158,  93,  86, 242, 211, 171, 
		68,  17, 146, 217,  35,  32,  46, 137, 180, 124, 184,  38, 119, 153, 227, 165, 
		103,  74, 237, 222, 197,  49, 254,  24,  13,  99, 140, 128, 192, 247, 112,   7 
		};
	
		static byte[] alogtable = {
		1,   3,   5,  15,  17,  51,  85, 255,  26,  46, 114, 150, 161, 248,  19,  53, 
		95, 225,  56,  72, 216, 115, 149, 164, 247,   2,   6,  10,  30,  34, 102, 170, 
		229,  52,  92, 228,  55,  89, 235,  38, 106, 190, 217, 112, 144, 171, 230,  49, 
		83, 245,   4,  12,  20,  60,  68, 204,  79, 209, 104, 184, 211, 110, 178, 205, 
		76, 212, 103, 169, 224,  59,  77, 215,  98, 166, 241,   8,  24,  40, 120, 136, 
		131, 158, 185, 208, 107, 189, 220, 127, 129, 152, 179, 206,  73, 219, 118, 154, 
		181, 196,  87, 249,  16,  48,  80, 240,  11,  29,  39, 105, 187, 214,  97, 163, 
		254,  25,  43, 125, 135, 146, 173, 236,  47, 113, 147, 174, 233,  32,  96, 160, 
		251,  22,  58,  78, 210, 109, 183, 194,  93, 231,  50,  86, 250,  21,  63,  65, 
		195,  94, 226,  61,  71, 201,  64, 192,  91, 237,  44, 116, 156, 191, 218, 117, 
		159, 186, 213, 100, 172, 239,  42, 126, 130, 157, 188, 223, 122, 142, 137, 128, 
		155, 182, 193,  88, 232,  35, 101, 175, 234,  37, 111, 177, 200,  67, 197,  84, 
		252,  31,  33,  99, 165, 244,   7,   9,  27,  45, 119, 153, 176, 203,  70, 202, 
		69, 207,  74, 222, 121, 139, 134, 145, 168, 227,  62,  66, 198,  81, 243,  14, 
		18,  54,  90, 238,  41, 123, 141, 140, 143, 138, 133, 148, 167, 242,  13,  23, 
		57,  75, 221, 124, 132, 151, 162, 253,  28,  36, 108, 180, 199,  82, 246,   1 
		};*/
	
		static readonly byte[] mult2 = { 
		0x00, 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C, 0x0E, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x1C, 0x1E, 
		0x20, 0x22, 0x24, 0x26, 0x28, 0x2A, 0x2C, 0x2E, 0x30, 0x32, 0x34, 0x36, 0x38, 0x3A, 0x3C, 0x3E, 
		0x40, 0x42, 0x44, 0x46, 0x48, 0x4A, 0x4C, 0x4E, 0x50, 0x52, 0x54, 0x56, 0x58, 0x5A, 0x5C, 0x5E, 
		0x60, 0x62, 0x64, 0x66, 0x68, 0x6A, 0x6C, 0x6E, 0x70, 0x72, 0x74, 0x76, 0x78, 0x7A, 0x7C, 0x7E, 
		0x80, 0x82, 0x84, 0x86, 0x88, 0x8A, 0x8C, 0x8E, 0x90, 0x92, 0x94, 0x96, 0x98, 0x9A, 0x9C, 0x9E, 
		0xA0, 0xA2, 0xA4, 0xA6, 0xA8, 0xAA, 0xAC, 0xAE, 0xB0, 0xB2, 0xB4, 0xB6, 0xB8, 0xBA, 0xBC, 0xBE, 
		0xC0, 0xC2, 0xC4, 0xC6, 0xC8, 0xCA, 0xCC, 0xCE, 0xD0, 0xD2, 0xD4, 0xD6, 0xD8, 0xDA, 0xDC, 0xDE, 
		0xE0, 0xE2, 0xE4, 0xE6, 0xE8, 0xEA, 0xEC, 0xEE, 0xF0, 0xF2, 0xF4, 0xF6, 0xF8, 0xFA, 0xFC, 0xFE, 
		0x1B, 0x19, 0x1F, 0x1D, 0x13, 0x11, 0x17, 0x15, 0x0B, 0x09, 0x0F, 0x0D, 0x03, 0x01, 0x07, 0x05, 
		0x3B, 0x39, 0x3F, 0x3D, 0x33, 0x31, 0x37, 0x35, 0x2B, 0x29, 0x2F, 0x2D, 0x23, 0x21, 0x27, 0x25, 
		0x5B, 0x59, 0x5F, 0x5D, 0x53, 0x51, 0x57, 0x55, 0x4B, 0x49, 0x4F, 0x4D, 0x43, 0x41, 0x47, 0x45, 
		0x7B, 0x79, 0x7F, 0x7D, 0x73, 0x71, 0x77, 0x75, 0x6B, 0x69, 0x6F, 0x6D, 0x63, 0x61, 0x67, 0x65, 
		0x9B, 0x99, 0x9F, 0x9D, 0x93, 0x91, 0x97, 0x95, 0x8B, 0x89, 0x8F, 0x8D, 0x83, 0x81, 0x87, 0x85, 
		0xBB, 0xB9, 0xBF, 0xBD, 0xB3, 0xB1, 0xB7, 0xB5, 0xAB, 0xA9, 0xAF, 0xAD, 0xA3, 0xA1, 0xA7, 0xA5, 
		0xDB, 0xD9, 0xDF, 0xDD, 0xD3, 0xD1, 0xD7, 0xD5, 0xCB, 0xC9, 0xCF, 0xCD, 0xC3, 0xC1, 0xC7, 0xC5, 
		0xFB, 0xF9, 0xFF, 0xFD, 0xF3, 0xF1, 0xF7, 0xF5, 0xEB, 0xE9, 0xEF, 0xED, 0xE3, 0xE1, 0xE7, 0xE5
		};
	
		static readonly byte[] mult3 = {
		0x00, 0x03, 0x06, 0x05, 0x0C, 0x0F, 0x0A, 0x09, 0x18, 0x1B, 0x1E, 0x1D, 0x14, 0x17, 0x12, 0x11, 
		0x30, 0x33, 0x36, 0x35, 0x3C, 0x3F, 0x3A, 0x39, 0x28, 0x2B, 0x2E, 0x2D, 0x24, 0x27, 0x22, 0x21, 
		0x60, 0x63, 0x66, 0x65, 0x6C, 0x6F, 0x6A, 0x69, 0x78, 0x7B, 0x7E, 0x7D, 0x74, 0x77, 0x72, 0x71, 
		0x50, 0x53, 0x56, 0x55, 0x5C, 0x5F, 0x5A, 0x59, 0x48, 0x4B, 0x4E, 0x4D, 0x44, 0x47, 0x42, 0x41, 
		0xC0, 0xC3, 0xC6, 0xC5, 0xCC, 0xCF, 0xCA, 0xC9, 0xD8, 0xDB, 0xDE, 0xDD, 0xD4, 0xD7, 0xD2, 0xD1, 
		0xF0, 0xF3, 0xF6, 0xF5, 0xFC, 0xFF, 0xFA, 0xF9, 0xE8, 0xEB, 0xEE, 0xED, 0xE4, 0xE7, 0xE2, 0xE1, 
		0xA0, 0xA3, 0xA6, 0xA5, 0xAC, 0xAF, 0xAA, 0xA9, 0xB8, 0xBB, 0xBE, 0xBD, 0xB4, 0xB7, 0xB2, 0xB1, 
		0x90, 0x93, 0x96, 0x95, 0x9C, 0x9F, 0x9A, 0x99, 0x88, 0x8B, 0x8E, 0x8D, 0x84, 0x87, 0x82, 0x81, 
		0x9B, 0x98, 0x9D, 0x9E, 0x97, 0x94, 0x91, 0x92, 0x83, 0x80, 0x85, 0x86, 0x8F, 0x8C, 0x89, 0x8A, 
		0xAB, 0xA8, 0xAD, 0xAE, 0xA7, 0xA4, 0xA1, 0xA2, 0xB3, 0xB0, 0xB5, 0xB6, 0xBF, 0xBC, 0xB9, 0xBA, 
		0xFB, 0xF8, 0xFD, 0xFE, 0xF7, 0xF4, 0xF1, 0xF2, 0xE3, 0xE0, 0xE5, 0xE6, 0xEF, 0xEC, 0xE9, 0xEA, 
		0xCB, 0xC8, 0xCD, 0xCE, 0xC7, 0xC4, 0xC1, 0xC2, 0xD3, 0xD0, 0xD5, 0xD6, 0xDF, 0xDC, 0xD9, 0xDA, 
		0x5B, 0x58, 0x5D, 0x5E, 0x57, 0x54, 0x51, 0x52, 0x43, 0x40, 0x45, 0x46, 0x4F, 0x4C, 0x49, 0x4A, 
		0x6B, 0x68, 0x6D, 0x6E, 0x67, 0x64, 0x61, 0x62, 0x73, 0x70, 0x75, 0x76, 0x7F, 0x7C, 0x79, 0x7A, 
		0x3B, 0x38, 0x3D, 0x3E, 0x37, 0x34, 0x31, 0x32, 0x23, 0x20, 0x25, 0x26, 0x2F, 0x2C, 0x29, 0x2A, 
		0x0B, 0x08, 0x0D, 0x0E, 0x07, 0x04, 0x01, 0x02, 0x13, 0x10, 0x15, 0x16, 0x1F, 0x1C, 0x19, 0x1A 
		};
	
		static readonly byte[] multE = {
		0x00, 0x0E, 0x1C, 0x12, 0x38, 0x36, 0x24, 0x2A, 0x70, 0x7E, 0x6C, 0x62, 0x48, 0x46, 0x54, 0x5A, 
		0xE0, 0xEE, 0xFC, 0xF2, 0xD8, 0xD6, 0xC4, 0xCA, 0x90, 0x9E, 0x8C, 0x82, 0xA8, 0xA6, 0xB4, 0xBA, 
		0xDB, 0xD5, 0xC7, 0xC9, 0xE3, 0xED, 0xFF, 0xF1, 0xAB, 0xA5, 0xB7, 0xB9, 0x93, 0x9D, 0x8F, 0x81, 
		0x3B, 0x35, 0x27, 0x29, 0x03, 0x0D, 0x1F, 0x11, 0x4B, 0x45, 0x57, 0x59, 0x73, 0x7D, 0x6F, 0x61, 
		0xAD, 0xA3, 0xB1, 0xBF, 0x95, 0x9B, 0x89, 0x87, 0xDD, 0xD3, 0xC1, 0xCF, 0xE5, 0xEB, 0xF9, 0xF7, 
		0x4D, 0x43, 0x51, 0x5F, 0x75, 0x7B, 0x69, 0x67, 0x3D, 0x33, 0x21, 0x2F, 0x05, 0x0B, 0x19, 0x17, 
		0x76, 0x78, 0x6A, 0x64, 0x4E, 0x40, 0x52, 0x5C, 0x06, 0x08, 0x1A, 0x14, 0x3E, 0x30, 0x22, 0x2C, 
		0x96, 0x98, 0x8A, 0x84, 0xAE, 0xA0, 0xB2, 0xBC, 0xE6, 0xE8, 0xFA, 0xF4, 0xDE, 0xD0, 0xC2, 0xCC, 
		0x41, 0x4F, 0x5D, 0x53, 0x79, 0x77, 0x65, 0x6B, 0x31, 0x3F, 0x2D, 0x23, 0x09, 0x07, 0x15, 0x1B, 
		0xA1, 0xAF, 0xBD, 0xB3, 0x99, 0x97, 0x85, 0x8B, 0xD1, 0xDF, 0xCD, 0xC3, 0xE9, 0xE7, 0xF5, 0xFB, 
		0x9A, 0x94, 0x86, 0x88, 0xA2, 0xAC, 0xBE, 0xB0, 0xEA, 0xE4, 0xF6, 0xF8, 0xD2, 0xDC, 0xCE, 0xC0, 
		0x7A, 0x74, 0x66, 0x68, 0x42, 0x4C, 0x5E, 0x50, 0x0A, 0x04, 0x16, 0x18, 0x32, 0x3C, 0x2E, 0x20, 
		0xEC, 0xE2, 0xF0, 0xFE, 0xD4, 0xDA, 0xC8, 0xC6, 0x9C, 0x92, 0x80, 0x8E, 0xA4, 0xAA, 0xB8, 0xB6, 
		0x0C, 0x02, 0x10, 0x1E, 0x34, 0x3A, 0x28, 0x26, 0x7C, 0x72, 0x60, 0x6E, 0x44, 0x4A, 0x58, 0x56, 
		0x37, 0x39, 0x2B, 0x25, 0x0F, 0x01, 0x13, 0x1D, 0x47, 0x49, 0x5B, 0x55, 0x7F, 0x71, 0x63, 0x6D, 
		0xD7, 0xD9, 0xCB, 0xC5, 0xEF, 0xE1, 0xF3, 0xFD, 0xA7, 0xA9, 0xBB, 0xB5, 0x9F, 0x91, 0x83, 0x8D, 
		};
	
		static readonly byte[] multB = {
		0x00, 0x0B, 0x16, 0x1D, 0x2C, 0x27, 0x3A, 0x31, 0x58, 0x53, 0x4E, 0x45, 0x74, 0x7F, 0x62, 0x69, 
		0xB0, 0xBB, 0xA6, 0xAD, 0x9C, 0x97, 0x8A, 0x81, 0xE8, 0xE3, 0xFE, 0xF5, 0xC4, 0xCF, 0xD2, 0xD9, 
		0x7B, 0x70, 0x6D, 0x66, 0x57, 0x5C, 0x41, 0x4A, 0x23, 0x28, 0x35, 0x3E, 0x0F, 0x04, 0x19, 0x12, 
		0xCB, 0xC0, 0xDD, 0xD6, 0xE7, 0xEC, 0xF1, 0xFA, 0x93, 0x98, 0x85, 0x8E, 0xBF, 0xB4, 0xA9, 0xA2, 
		0xF6, 0xFD, 0xE0, 0xEB, 0xDA, 0xD1, 0xCC, 0xC7, 0xAE, 0xA5, 0xB8, 0xB3, 0x82, 0x89, 0x94, 0x9F, 
		0x46, 0x4D, 0x50, 0x5B, 0x6A, 0x61, 0x7C, 0x77, 0x1E, 0x15, 0x08, 0x03, 0x32, 0x39, 0x24, 0x2F, 
		0x8D, 0x86, 0x9B, 0x90, 0xA1, 0xAA, 0xB7, 0xBC, 0xD5, 0xDE, 0xC3, 0xC8, 0xF9, 0xF2, 0xEF, 0xE4, 
		0x3D, 0x36, 0x2B, 0x20, 0x11, 0x1A, 0x07, 0x0C, 0x65, 0x6E, 0x73, 0x78, 0x49, 0x42, 0x5F, 0x54, 
		0xF7, 0xFC, 0xE1, 0xEA, 0xDB, 0xD0, 0xCD, 0xC6, 0xAF, 0xA4, 0xB9, 0xB2, 0x83, 0x88, 0x95, 0x9E, 
		0x47, 0x4C, 0x51, 0x5A, 0x6B, 0x60, 0x7D, 0x76, 0x1F, 0x14, 0x09, 0x02, 0x33, 0x38, 0x25, 0x2E, 
		0x8C, 0x87, 0x9A, 0x91, 0xA0, 0xAB, 0xB6, 0xBD, 0xD4, 0xDF, 0xC2, 0xC9, 0xF8, 0xF3, 0xEE, 0xE5, 
		0x3C, 0x37, 0x2A, 0x21, 0x10, 0x1B, 0x06, 0x0D, 0x64, 0x6F, 0x72, 0x79, 0x48, 0x43, 0x5E, 0x55, 
		0x01, 0x0A, 0x17, 0x1C, 0x2D, 0x26, 0x3B, 0x30, 0x59, 0x52, 0x4F, 0x44, 0x75, 0x7E, 0x63, 0x68, 
		0xB1, 0xBA, 0xA7, 0xAC, 0x9D, 0x96, 0x8B, 0x80, 0xE9, 0xE2, 0xFF, 0xF4, 0xC5, 0xCE, 0xD3, 0xD8, 
		0x7A, 0x71, 0x6C, 0x67, 0x56, 0x5D, 0x40, 0x4B, 0x22, 0x29, 0x34, 0x3F, 0x0E, 0x05, 0x18, 0x13, 
		0xCA, 0xC1, 0xDC, 0xD7, 0xE6, 0xED, 0xF0, 0xFB, 0x92, 0x99, 0x84, 0x8F, 0xBE, 0xB5, 0xA8, 0xA3, 
		};
	
		static readonly byte[] multD = {
		0x00, 0x0D, 0x1A, 0x17, 0x34, 0x39, 0x2E, 0x23, 0x68, 0x65, 0x72, 0x7F, 0x5C, 0x51, 0x46, 0x4B, 
		0xD0, 0xDD, 0xCA, 0xC7, 0xE4, 0xE9, 0xFE, 0xF3, 0xB8, 0xB5, 0xA2, 0xAF, 0x8C, 0x81, 0x96, 0x9B, 
		0xBB, 0xB6, 0xA1, 0xAC, 0x8F, 0x82, 0x95, 0x98, 0xD3, 0xDE, 0xC9, 0xC4, 0xE7, 0xEA, 0xFD, 0xF0, 
		0x6B, 0x66, 0x71, 0x7C, 0x5F, 0x52, 0x45, 0x48, 0x03, 0x0E, 0x19, 0x14, 0x37, 0x3A, 0x2D, 0x20, 
		0x6D, 0x60, 0x77, 0x7A, 0x59, 0x54, 0x43, 0x4E, 0x05, 0x08, 0x1F, 0x12, 0x31, 0x3C, 0x2B, 0x26, 
		0xBD, 0xB0, 0xA7, 0xAA, 0x89, 0x84, 0x93, 0x9E, 0xD5, 0xD8, 0xCF, 0xC2, 0xE1, 0xEC, 0xFB, 0xF6, 
		0xD6, 0xDB, 0xCC, 0xC1, 0xE2, 0xEF, 0xF8, 0xF5, 0xBE, 0xB3, 0xA4, 0xA9, 0x8A, 0x87, 0x90, 0x9D, 
		0x06, 0x0B, 0x1C, 0x11, 0x32, 0x3F, 0x28, 0x25, 0x6E, 0x63, 0x74, 0x79, 0x5A, 0x57, 0x40, 0x4D, 
		0xDA, 0xD7, 0xC0, 0xCD, 0xEE, 0xE3, 0xF4, 0xF9, 0xB2, 0xBF, 0xA8, 0xA5, 0x86, 0x8B, 0x9C, 0x91, 
		0x0A, 0x07, 0x10, 0x1D, 0x3E, 0x33, 0x24, 0x29, 0x62, 0x6F, 0x78, 0x75, 0x56, 0x5B, 0x4C, 0x41, 
		0x61, 0x6C, 0x7B, 0x76, 0x55, 0x58, 0x4F, 0x42, 0x09, 0x04, 0x13, 0x1E, 0x3D, 0x30, 0x27, 0x2A, 
		0xB1, 0xBC, 0xAB, 0xA6, 0x85, 0x88, 0x9F, 0x92, 0xD9, 0xD4, 0xC3, 0xCE, 0xED, 0xE0, 0xF7, 0xFA, 
		0xB7, 0xBA, 0xAD, 0xA0, 0x83, 0x8E, 0x99, 0x94, 0xDF, 0xD2, 0xC5, 0xC8, 0xEB, 0xE6, 0xF1, 0xFC, 
		0x67, 0x6A, 0x7D, 0x70, 0x53, 0x5E, 0x49, 0x44, 0x0F, 0x02, 0x15, 0x18, 0x3B, 0x36, 0x21, 0x2C, 
		0x0C, 0x01, 0x16, 0x1B, 0x38, 0x35, 0x22, 0x2F, 0x64, 0x69, 0x7E, 0x73, 0x50, 0x5D, 0x4A, 0x47, 
		0xDC, 0xD1, 0xC6, 0xCB, 0xE8, 0xE5, 0xF2, 0xFF, 0xB4, 0xB9, 0xAE, 0xA3, 0x80, 0x8D, 0x9A, 0x97, 
		};
	
		static readonly byte[] mult9 = {
		0x00, 0x09, 0x12, 0x1B, 0x24, 0x2D, 0x36, 0x3F, 0x48, 0x41, 0x5A, 0x53, 0x6C, 0x65, 0x7E, 0x77, 
		0x90, 0x99, 0x82, 0x8B, 0xB4, 0xBD, 0xA6, 0xAF, 0xD8, 0xD1, 0xCA, 0xC3, 0xFC, 0xF5, 0xEE, 0xE7, 
		0x3B, 0x32, 0x29, 0x20, 0x1F, 0x16, 0x0D, 0x04, 0x73, 0x7A, 0x61, 0x68, 0x57, 0x5E, 0x45, 0x4C, 
		0xAB, 0xA2, 0xB9, 0xB0, 0x8F, 0x86, 0x9D, 0x94, 0xE3, 0xEA, 0xF1, 0xF8, 0xC7, 0xCE, 0xD5, 0xDC, 
		0x76, 0x7F, 0x64, 0x6D, 0x52, 0x5B, 0x40, 0x49, 0x3E, 0x37, 0x2C, 0x25, 0x1A, 0x13, 0x08, 0x01, 
		0xE6, 0xEF, 0xF4, 0xFD, 0xC2, 0xCB, 0xD0, 0xD9, 0xAE, 0xA7, 0xBC, 0xB5, 0x8A, 0x83, 0x98, 0x91, 
		0x4D, 0x44, 0x5F, 0x56, 0x69, 0x60, 0x7B, 0x72, 0x05, 0x0C, 0x17, 0x1E, 0x21, 0x28, 0x33, 0x3A, 
		0xDD, 0xD4, 0xCF, 0xC6, 0xF9, 0xF0, 0xEB, 0xE2, 0x95, 0x9C, 0x87, 0x8E, 0xB1, 0xB8, 0xA3, 0xAA, 
		0xEC, 0xE5, 0xFE, 0xF7, 0xC8, 0xC1, 0xDA, 0xD3, 0xA4, 0xAD, 0xB6, 0xBF, 0x80, 0x89, 0x92, 0x9B, 
		0x7C, 0x75, 0x6E, 0x67, 0x58, 0x51, 0x4A, 0x43, 0x34, 0x3D, 0x26, 0x2F, 0x10, 0x19, 0x02, 0x0B, 
		0xD7, 0xDE, 0xC5, 0xCC, 0xF3, 0xFA, 0xE1, 0xE8, 0x9F, 0x96, 0x8D, 0x84, 0xBB, 0xB2, 0xA9, 0xA0, 
		0x47, 0x4E, 0x55, 0x5C, 0x63, 0x6A, 0x71, 0x78, 0x0F, 0x06, 0x1D, 0x14, 0x2B, 0x22, 0x39, 0x30, 
		0x9A, 0x93, 0x88, 0x81, 0xBE, 0xB7, 0xAC, 0xA5, 0xD2, 0xDB, 0xC0, 0xC9, 0xF6, 0xFF, 0xE4, 0xED, 
		0x0A, 0x03, 0x18, 0x11, 0x2E, 0x27, 0x3C, 0x35, 0x42, 0x4B, 0x50, 0x59, 0x66, 0x6F, 0x74, 0x7D, 
		0xA1, 0xA8, 0xB3, 0xBA, 0x85, 0x8C, 0x97, 0x9E, 0xE9, 0xE0, 0xFB, 0xF2, 0xCD, 0xC4, 0xDF, 0xD6, 
		0x31, 0x38, 0x23, 0x2A, 0x15, 0x1C, 0x07, 0x0E, 0x79, 0x70, 0x6B, 0x62, 0x5D, 0x54, 0x4F, 0x46, 
		};
	
	}
}
