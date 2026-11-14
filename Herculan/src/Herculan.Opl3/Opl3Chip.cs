/* Nuked OPL3
 * Copyright (C) 2013-2020 Nuke.YKT
 *
 * This file is part of Nuked OPL3.
 *
 * Nuked OPL3 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 2.1
 * of the License, or (at your option) any later version.
 *
 * Nuked OPL3 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with Nuked OPL3. If not, see <https://www.gnu.org/licenses/>.

 *  Nuked OPL3 emulator.
 *  Thanks:
 *      MAME Development Team(Jarek Burczynski, Tatsuyuki Satoh):
 *          Feedback and Rhythm part calculation information.
 *      forums.submarine.org.uk(carbon14, opl3):
 *          Tremolo and phase generator calculation information.
 *      OPLx decapsulated(Matthew Gambrell, Olli Niemitalo):
 *          OPL2 ROMs.
 *      siliconpr0n.org(John McMaster, digshadow):
 *          YMF262 and VRC VII decaps and die shots.
 *
 * version: 1.8
 *
 * C# port of opl3.c and opl3.h, from https://github.com/nukeykt/Nuked-OPL3 at commit
 * 765ec962e473aeb767e4cba74ffdc8f588ffbfe8, made for HERCULAN. The changes from the C source:
 *  - The structs are classes, their pointers to slots, channels and the chip object references.
 *  - Each int16_t a slot's mod or a channel's out[] pointed at (a slot's out and fbmod, the chip's zeromod)
 *    is a Signal object, and the pointer a reference to it.
 *  - A slot's trem, a uint8_t pointer to the chip's tremolo or to zeromod's low byte, is the bool trem_on:
 *    the tremolo when set, 0 when clear (zeromod is never written, so its low byte is always 0).
 *  - The function table envelope_sin is a switch on the waveform.
 *  - The slot's eg_inc and eg_rate, which the C source declares and never uses, are left out.
 *  - The OPL_ENABLE_STEREOEXT code is left out, as the C source compiles it by default (0), and with it
 *    OPL_QUIRK_CHANNELSAMPLEDELAY is on, as the C source then sets it.
 *  - The functions keep their names without the OPL3_ prefix, the fields their names, and C's implicit
 *    conversions are written as casts.
 */

namespace Herculan.Opl3;

/// <summary>
/// One emulated YMF262 (OPL3): Nuked-OPL3 1.8's <c>opl3_chip</c> and the functions over it. It produces one stereo sample
/// per <see cref="Generate"/> at the chip's own rate, <see cref="NativeRate"/>; the resampled functions convert to the rate
/// <see cref="Reset"/> was given.
/// </summary>
public sealed class Opl3Chip {
	/// <summary>The chip's own sample rate, its 14.31818 MHz clock over 288.</summary>
	public const int NativeRate = 49716;

	private const int OPL_WRITEBUF_SIZE = 1024;
	private const int OPL_WRITEBUF_DELAY = 2;
	private const int RSM_FRAC = 10;

	/* Channel types */
	private const byte ch_2op = 0;
	private const byte ch_4op = 1;
	private const byte ch_4op2 = 2;
	private const byte ch_drum = 3;

	/* Envelope key types */
	private const byte egk_norm = 0x01;
	private const byte egk_drum = 0x02;

	private const byte envelope_gen_num_attack = 0;
	private const byte envelope_gen_num_decay = 1;
	private const byte envelope_gen_num_sustain = 2;
	private const byte envelope_gen_num_release = 3;

	/*
	    logsin table
	*/
	private static readonly ushort[] logsinrom = [
		0x859, 0x6c3, 0x607, 0x58b, 0x52e, 0x4e4, 0x4a6, 0x471,
		0x443, 0x41a, 0x3f5, 0x3d3, 0x3b5, 0x398, 0x37e, 0x365,
		0x34e, 0x339, 0x324, 0x311, 0x2ff, 0x2ed, 0x2dc, 0x2cd,
		0x2bd, 0x2af, 0x2a0, 0x293, 0x286, 0x279, 0x26d, 0x261,
		0x256, 0x24b, 0x240, 0x236, 0x22c, 0x222, 0x218, 0x20f,
		0x206, 0x1fd, 0x1f5, 0x1ec, 0x1e4, 0x1dc, 0x1d4, 0x1cd,
		0x1c5, 0x1be, 0x1b7, 0x1b0, 0x1a9, 0x1a2, 0x19b, 0x195,
		0x18f, 0x188, 0x182, 0x17c, 0x177, 0x171, 0x16b, 0x166,
		0x160, 0x15b, 0x155, 0x150, 0x14b, 0x146, 0x141, 0x13c,
		0x137, 0x133, 0x12e, 0x129, 0x125, 0x121, 0x11c, 0x118,
		0x114, 0x10f, 0x10b, 0x107, 0x103, 0x0ff, 0x0fb, 0x0f8,
		0x0f4, 0x0f0, 0x0ec, 0x0e9, 0x0e5, 0x0e2, 0x0de, 0x0db,
		0x0d7, 0x0d4, 0x0d1, 0x0cd, 0x0ca, 0x0c7, 0x0c4, 0x0c1,
		0x0be, 0x0bb, 0x0b8, 0x0b5, 0x0b2, 0x0af, 0x0ac, 0x0a9,
		0x0a7, 0x0a4, 0x0a1, 0x09f, 0x09c, 0x099, 0x097, 0x094,
		0x092, 0x08f, 0x08d, 0x08a, 0x088, 0x086, 0x083, 0x081,
		0x07f, 0x07d, 0x07a, 0x078, 0x076, 0x074, 0x072, 0x070,
		0x06e, 0x06c, 0x06a, 0x068, 0x066, 0x064, 0x062, 0x060,
		0x05e, 0x05c, 0x05b, 0x059, 0x057, 0x055, 0x053, 0x052,
		0x050, 0x04e, 0x04d, 0x04b, 0x04a, 0x048, 0x046, 0x045,
		0x043, 0x042, 0x040, 0x03f, 0x03e, 0x03c, 0x03b, 0x039,
		0x038, 0x037, 0x035, 0x034, 0x033, 0x031, 0x030, 0x02f,
		0x02e, 0x02d, 0x02b, 0x02a, 0x029, 0x028, 0x027, 0x026,
		0x025, 0x024, 0x023, 0x022, 0x021, 0x020, 0x01f, 0x01e,
		0x01d, 0x01c, 0x01b, 0x01a, 0x019, 0x018, 0x017, 0x017,
		0x016, 0x015, 0x014, 0x014, 0x013, 0x012, 0x011, 0x011,
		0x010, 0x00f, 0x00f, 0x00e, 0x00d, 0x00d, 0x00c, 0x00c,
		0x00b, 0x00a, 0x00a, 0x009, 0x009, 0x008, 0x008, 0x007,
		0x007, 0x007, 0x006, 0x006, 0x005, 0x005, 0x005, 0x004,
		0x004, 0x004, 0x003, 0x003, 0x003, 0x002, 0x002, 0x002,
		0x002, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001,
		0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
	];

	/*
	    exp table
	*/
	private static readonly ushort[] exprom = [
		0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4,
		0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
		0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f,
		0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
		0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e,
		0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
		0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0,
		0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
		0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695,
		0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
		0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e,
		0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
		0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609,
		0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
		0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8,
		0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
		0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589,
		0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
		0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d,
		0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
		0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514,
		0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
		0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc,
		0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
		0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8,
		0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
		0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475,
		0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
		0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445,
		0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
		0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416,
		0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400,
	];

	/*
	    freq mult table multiplied by 2

	    1/2, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 12, 12, 15, 15
	*/
	private static readonly byte[] mt = [1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30];

	/*
	    ksl table
	*/
	private static readonly byte[] kslrom = [0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64];

	private static readonly byte[] kslshift = [8, 1, 2, 0];

	/*
	    envelope generator constants
	*/
	private static readonly byte[,] eg_incstep = {
		{ 0, 0, 0, 0 },
		{ 1, 0, 0, 0 },
		{ 1, 0, 1, 0 },
		{ 1, 1, 1, 0 },
	};

	/*
	    address decoding
	*/
	private static readonly sbyte[] ad_slot = [
		0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1,
		12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
	];

	private static readonly byte[] ch_slot = [0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32];

	/// <summary>An <c>int16_t</c> that a slot's <c>mod</c> or a channel's <c>out[]</c> points at.</summary>
	private sealed class Signal {
		public short value;
	}

	private sealed class Slot {
		public Channel channel = null!;
		public Opl3Chip chip = null!;
		public readonly Signal @out = new();
		public readonly Signal fbmod = new();
		public Signal mod = null!;
		public short prout;
		public ushort eg_rout;
		public ushort eg_out;
		public byte eg_gen;
		public byte eg_ksl;
		public bool trem_on;
		public byte reg_vib;
		public byte reg_type;
		public byte reg_ksr;
		public byte reg_mult;
		public byte reg_ksl;
		public byte reg_tl;
		public byte reg_ar;
		public byte reg_dr;
		public byte reg_sl;
		public byte reg_rr;
		public byte reg_wf;
		public byte key;
		public uint pg_reset;
		public uint pg_phase;
		public ushort pg_phase_out;
		public byte slot_num;
	}

	private sealed class Channel {
		public readonly Slot[] slotz = new Slot[2];
		public Channel? pair;
		public Opl3Chip chip = null!;
		public readonly Signal[] @out = new Signal[4];
		public byte chtype;
		public ushort f_num;
		public ushort f_num_reg;
		public byte block;
		public byte block_reg;
		public byte fb;
		public byte con;
		public byte alg;
		public byte ksv;
		public ushort cha, chb;
		public ushort chc, chd;
		public byte ch_num;
	}

	private struct WriteBuf {
		public ulong time;
		public ushort reg;
		public byte data;
	}

	private readonly Channel[] channel = new Channel[18];
	private readonly Slot[] slot = new Slot[36];
	private ushort timer;
	private ulong eg_timer;
	private byte eg_timerrem;
	private byte eg_state;
	private byte eg_add;
	private byte eg_timer_lo;
	private byte newm;
	private byte nts;
	private byte rhy;
	private byte vibpos;
	private byte vibshift;
	private byte tremolo;
	private byte tremolopos;
	private byte tremoloshift;
	private uint noise;
	private readonly Signal zeromod = new();
	private readonly int[] mixbuff = new int[4];
	private byte rm_hh_bit2;
	private byte rm_hh_bit3;
	private byte rm_hh_bit7;
	private byte rm_hh_bit8;
	private byte rm_tc_bit3;
	private byte rm_tc_bit5;

	/* OPL3L */
	private int rateratio;
	private int samplecnt;
	private readonly short[] oldsamples = new short[4];
	private readonly short[] samples = new short[4];

	private ulong writebuf_samplecnt;
	private uint writebuf_cur;
	private uint writebuf_last;
	private ulong writebuf_lasttime;
	private readonly WriteBuf[] writebuf = new WriteBuf[OPL_WRITEBUF_SIZE];

	/// <summary>A chip reset for output at <paramref name="samplerate"/>, as <see cref="Reset"/> leaves it.</summary>
	public Opl3Chip(uint samplerate = NativeRate) {
		Reset(samplerate);
	}

	/*
	    Envelope generator
	*/

	private static short EnvelopeCalcExp(uint level) {
		if (level > 0x1fff) {
			level = 0x1fff;
		}

		return (short)((exprom[level & 0xffu] << 1) >> (int)(level >> 8));
	}

	private static short EnvelopeCalcSin0(ushort phase, ushort envelope) {
		ushort @out = 0;
		ushort neg = 0;
		phase &= 0x3ff;
		if ((phase & 0x200) != 0) {
			neg = 0xffff;
		}

		if ((phase & 0x100) != 0) {
			@out = logsinrom[(phase & 0xffu) ^ 0xffu];
		} else {
			@out = logsinrom[phase & 0xffu];
		}

		return (short)(EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
	}

	private static short EnvelopeCalcSin1(ushort phase, ushort envelope) {
		ushort @out = 0;
		phase &= 0x3ff;
		if ((phase & 0x200) != 0) {
			@out = 0x1000;
		} else if ((phase & 0x100) != 0) {
			@out = logsinrom[(phase & 0xffu) ^ 0xffu];
		} else {
			@out = logsinrom[phase & 0xffu];
		}

		return EnvelopeCalcExp((uint)(@out + (envelope << 3)));
	}

	private static short EnvelopeCalcSin2(ushort phase, ushort envelope) {
		ushort @out = 0;
		phase &= 0x3ff;
		if ((phase & 0x100) != 0) {
			@out = logsinrom[(phase & 0xffu) ^ 0xffu];
		} else {
			@out = logsinrom[phase & 0xffu];
		}

		return EnvelopeCalcExp((uint)(@out + (envelope << 3)));
	}

	private static short EnvelopeCalcSin3(ushort phase, ushort envelope) {
		ushort @out = 0;
		phase &= 0x3ff;
		if ((phase & 0x100) != 0) {
			@out = 0x1000;
		} else {
			@out = logsinrom[phase & 0xffu];
		}

		return EnvelopeCalcExp((uint)(@out + (envelope << 3)));
	}

	private static short EnvelopeCalcSin4(ushort phase, ushort envelope) {
		ushort @out = 0;
		ushort neg = 0;
		phase &= 0x3ff;
		if ((phase & 0x300) == 0x100) {
			neg = 0xffff;
		}

		if ((phase & 0x200) != 0) {
			@out = 0x1000;
		} else if ((phase & 0x80) != 0) {
			@out = logsinrom[((phase ^ 0xffu) << 1) & 0xffu];
		} else {
			@out = logsinrom[((uint)phase << 1) & 0xffu];
		}

		return (short)(EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
	}

	private static short EnvelopeCalcSin5(ushort phase, ushort envelope) {
		ushort @out = 0;
		phase &= 0x3ff;
		if ((phase & 0x200) != 0) {
			@out = 0x1000;
		} else if ((phase & 0x80) != 0) {
			@out = logsinrom[((phase ^ 0xffu) << 1) & 0xffu];
		} else {
			@out = logsinrom[((uint)phase << 1) & 0xffu];
		}

		return EnvelopeCalcExp((uint)(@out + (envelope << 3)));
	}

	private static short EnvelopeCalcSin6(ushort phase, ushort envelope) {
		ushort neg = 0;
		phase &= 0x3ff;
		if ((phase & 0x200) != 0) {
			neg = 0xffff;
		}

		return (short)(EnvelopeCalcExp((uint)(envelope << 3)) ^ neg);
	}

	private static short EnvelopeCalcSin7(ushort phase, ushort envelope) {
		ushort @out = 0;
		ushort neg = 0;
		phase &= 0x3ff;
		if ((phase & 0x200) != 0) {
			neg = 0xffff;
			phase = (ushort)((phase & 0x1ff) ^ 0x1ff);
		}

		@out = (ushort)(phase << 3);
		return (short)(EnvelopeCalcExp((uint)(@out + (envelope << 3))) ^ neg);
	}

	/// <summary><c>envelope_sin[wf](phase, envelope)</c>.</summary>
	private static short EnvelopeSin(byte wf, ushort phase, ushort envelope) => wf switch {
		0 => EnvelopeCalcSin0(phase, envelope),
		1 => EnvelopeCalcSin1(phase, envelope),
		2 => EnvelopeCalcSin2(phase, envelope),
		3 => EnvelopeCalcSin3(phase, envelope),
		4 => EnvelopeCalcSin4(phase, envelope),
		5 => EnvelopeCalcSin5(phase, envelope),
		6 => EnvelopeCalcSin6(phase, envelope),
		_ => EnvelopeCalcSin7(phase, envelope),
	};

	private static void EnvelopeUpdateKSL(Slot slot) {
		short ksl = (short)((kslrom[slot.channel.f_num >> 6] << 2)
		                    - ((0x08 - slot.channel.block) << 5));
		if (ksl < 0) {
			ksl = 0;
		}

		slot.eg_ksl = (byte)ksl;
	}

	private static void EnvelopeCalc(Slot slot) {
		byte nonzero;
		byte rate;
		byte rate_hi;
		byte rate_lo;
		byte reg_rate = 0;
		byte ks;
		byte eg_shift, shift;
		ushort eg_rout;
		short eg_inc;
		byte eg_off;
		byte reset = 0;
		slot.eg_out = (ushort)(slot.eg_rout + (slot.reg_tl << 2)
		                       + (slot.eg_ksl >> kslshift[slot.reg_ksl]) + (slot.trem_on ? slot.chip.tremolo : 0));
		if (slot.key != 0 && slot.eg_gen == envelope_gen_num_release) {
			reset = 1;
			reg_rate = slot.reg_ar;
		} else {
			switch (slot.eg_gen) {
				case envelope_gen_num_attack:
					reg_rate = slot.reg_ar;
					break;
				case envelope_gen_num_decay:
					reg_rate = slot.reg_dr;
					break;
				case envelope_gen_num_sustain:
					if (slot.reg_type == 0) {
						reg_rate = slot.reg_rr;
					}

					break;
				case envelope_gen_num_release:
					reg_rate = slot.reg_rr;
					break;
			}
		}

		slot.pg_reset = reset;
		ks = (byte)(slot.channel.ksv >> ((slot.reg_ksr ^ 1) << 1));
		nonzero = (byte)(reg_rate != 0 ? 1 : 0);
		rate = (byte)(ks + (reg_rate << 2));
		rate_hi = (byte)(rate >> 2);
		rate_lo = (byte)(rate & 0x03);
		if ((rate_hi & 0x10) != 0) {
			rate_hi = 0x0f;
		}

		eg_shift = (byte)(rate_hi + slot.chip.eg_add);
		shift = 0;
		if (nonzero != 0) {
			if (rate_hi < 12) {
				if (slot.chip.eg_state != 0) {
					switch (eg_shift) {
						case 12:
							shift = 1;
							break;
						case 13:
							shift = (byte)((rate_lo >> 1) & 0x01);
							break;
						case 14:
							shift = (byte)(rate_lo & 0x01);
							break;
					}
				}
			} else {
				shift = (byte)((rate_hi & 0x03) + eg_incstep[rate_lo, slot.chip.eg_timer_lo]);
				if ((shift & 0x04) != 0) {
					shift = 0x03;
				}

				if (shift == 0) {
					shift = slot.chip.eg_state;
				}
			}
		}

		eg_rout = slot.eg_rout;
		eg_inc = 0;
		eg_off = 0;
		/* Instant attack */
		if (reset != 0 && rate_hi == 0x0f) {
			eg_rout = 0x00;
		}

		/* Envelope off */
		if ((slot.eg_rout & 0x1f8) == 0x1f8) {
			eg_off = 1;
		}

		if (slot.eg_gen != envelope_gen_num_attack && reset == 0 && eg_off != 0) {
			eg_rout = 0x1ff;
		}

		switch (slot.eg_gen) {
			case envelope_gen_num_attack:
				if (slot.eg_rout == 0) {
					slot.eg_gen = envelope_gen_num_decay;
				} else if (slot.key != 0 && shift > 0 && rate_hi != 0x0f) {
					eg_inc = (short)(~slot.eg_rout >> (4 - shift));
				}

				break;
			case envelope_gen_num_decay:
				if ((slot.eg_rout >> 4) == slot.reg_sl) {
					slot.eg_gen = envelope_gen_num_sustain;
				} else if (eg_off == 0 && reset == 0 && shift > 0) {
					eg_inc = (short)(1 << (shift - 1));
				}

				break;
			case envelope_gen_num_sustain:
			case envelope_gen_num_release:
				if (eg_off == 0 && reset == 0 && shift > 0) {
					eg_inc = (short)(1 << (shift - 1));
				}

				break;
		}

		slot.eg_rout = (ushort)((eg_rout + eg_inc) & 0x1ff);
		/* Key off */
		if (reset != 0) {
			slot.eg_gen = envelope_gen_num_attack;
		}

		if (slot.key == 0) {
			slot.eg_gen = envelope_gen_num_release;
		}
	}

	private static void EnvelopeKeyOn(Slot slot, byte type) {
		slot.key |= type;
	}

	private static void EnvelopeKeyOff(Slot slot, byte type) {
		slot.key &= (byte)~type;
	}

	/*
	    Phase Generator
	*/

	private static void PhaseGenerate(Slot slot) {
		Opl3Chip chip;
		ushort f_num;
		uint basefreq;
		byte rm_xor, n_bit;
		uint noise;
		ushort phase;

		chip = slot.chip;
		f_num = slot.channel.f_num;
		if (slot.reg_vib != 0) {
			sbyte range;
			byte vibpos;

			range = (sbyte)((f_num >> 7) & 7);
			vibpos = slot.chip.vibpos;

			if ((vibpos & 3) == 0) {
				range = 0;
			} else if ((vibpos & 1) != 0) {
				range >>= 1;
			}

			range >>= slot.chip.vibshift;

			if ((vibpos & 4) != 0) {
				range = (sbyte)-range;
			}

			f_num = (ushort)(f_num + range);
		}

		basefreq = (uint)(f_num << slot.channel.block) >> 1;
		phase = (ushort)(slot.pg_phase >> 9);
		if (slot.pg_reset != 0) {
			slot.pg_phase = 0;
		}

		slot.pg_phase += (basefreq * mt[slot.reg_mult]) >> 1;
		/* Rhythm mode */
		noise = chip.noise;
		slot.pg_phase_out = phase;
		if (slot.slot_num == 13) /* hh */ {
			chip.rm_hh_bit2 = (byte)((phase >> 2) & 1);
			chip.rm_hh_bit3 = (byte)((phase >> 3) & 1);
			chip.rm_hh_bit7 = (byte)((phase >> 7) & 1);
			chip.rm_hh_bit8 = (byte)((phase >> 8) & 1);
		}

		if (slot.slot_num == 17 && (chip.rhy & 0x20) != 0) /* tc */ {
			chip.rm_tc_bit3 = (byte)((phase >> 3) & 1);
			chip.rm_tc_bit5 = (byte)((phase >> 5) & 1);
		}

		if ((chip.rhy & 0x20) != 0) {
			rm_xor = (byte)((chip.rm_hh_bit2 ^ chip.rm_hh_bit7)
			                | (chip.rm_hh_bit3 ^ chip.rm_tc_bit5)
			                | (chip.rm_tc_bit3 ^ chip.rm_tc_bit5));
			switch (slot.slot_num) {
				case 13: /* hh */
					slot.pg_phase_out = (ushort)(rm_xor << 9);
					if ((rm_xor ^ (noise & 1)) != 0) {
						slot.pg_phase_out |= 0xd0;
					} else {
						slot.pg_phase_out |= 0x34;
					}

					break;
				case 16: /* sd */
					slot.pg_phase_out = (ushort)(((uint)chip.rm_hh_bit8 << 9)
					                             | ((chip.rm_hh_bit8 ^ (noise & 1)) << 8));
					break;
				case 17: /* tc */
					slot.pg_phase_out = (ushort)((rm_xor << 9) | 0x80);
					break;
			}
		}

		n_bit = (byte)(((noise >> 14) ^ noise) & 0x01);
		chip.noise = (noise >> 1) | ((uint)n_bit << 22);
	}

	/*
	    Slot
	*/

	private static void SlotWrite20(Slot slot, byte data) {
		slot.trem_on = ((data >> 7) & 0x01) != 0;
		slot.reg_vib = (byte)((data >> 6) & 0x01);
		slot.reg_type = (byte)((data >> 5) & 0x01);
		slot.reg_ksr = (byte)((data >> 4) & 0x01);
		slot.reg_mult = (byte)(data & 0x0f);
	}

	private static void SlotWrite40(Slot slot, byte data) {
		slot.reg_ksl = (byte)((data >> 6) & 0x03);
		slot.reg_tl = (byte)(data & 0x3f);
		EnvelopeUpdateKSL(slot);
	}

	private static void SlotWrite60(Slot slot, byte data) {
		slot.reg_ar = (byte)((data >> 4) & 0x0f);
		slot.reg_dr = (byte)(data & 0x0f);
	}

	private static void SlotWrite80(Slot slot, byte data) {
		slot.reg_sl = (byte)((data >> 4) & 0x0f);
		if (slot.reg_sl == 0x0f) {
			slot.reg_sl = 0x1f;
		}

		slot.reg_rr = (byte)(data & 0x0f);
	}

	private static void SlotWriteE0(Slot slot, byte data) {
		slot.reg_wf = (byte)(data & 0x07);
		if (slot.chip.newm == 0x00) {
			slot.reg_wf &= 0x03;
		}
	}

	private static void SlotGenerate(Slot slot) {
		slot.@out.value = EnvelopeSin(slot.reg_wf, (ushort)(slot.pg_phase_out + slot.mod.value), slot.eg_out);
	}

	private static void SlotCalcFB(Slot slot) {
		if (slot.channel.fb != 0x00) {
			slot.fbmod.value = (short)((slot.prout + slot.@out.value) >> (0x09 - slot.channel.fb));
		} else {
			slot.fbmod.value = 0;
		}

		slot.prout = slot.@out.value;
	}

	/*
	    Channel
	*/

	private void ChannelUpdateRhythm(byte data) {
		Channel channel6;
		Channel channel7;
		Channel channel8;
		byte chnum;

		rhy = (byte)(data & 0x3f);
		if ((rhy & 0x20) != 0) {
			channel6 = channel[6];
			channel7 = channel[7];
			channel8 = channel[8];
			channel6.@out[0] = channel6.slotz[1].@out;
			channel6.@out[1] = channel6.slotz[1].@out;
			channel6.@out[2] = zeromod;
			channel6.@out[3] = zeromod;
			channel7.@out[0] = channel7.slotz[0].@out;
			channel7.@out[1] = channel7.slotz[0].@out;
			channel7.@out[2] = channel7.slotz[1].@out;
			channel7.@out[3] = channel7.slotz[1].@out;
			channel8.@out[0] = channel8.slotz[0].@out;
			channel8.@out[1] = channel8.slotz[0].@out;
			channel8.@out[2] = channel8.slotz[1].@out;
			channel8.@out[3] = channel8.slotz[1].@out;
			for (chnum = 6; chnum < 9; chnum++) {
				channel[chnum].chtype = ch_drum;
			}

			ChannelSetupAlg(channel6);
			ChannelSetupAlg(channel7);
			ChannelSetupAlg(channel8);
			/* hh */
			if ((rhy & 0x01) != 0) {
				EnvelopeKeyOn(channel7.slotz[0], egk_drum);
			} else {
				EnvelopeKeyOff(channel7.slotz[0], egk_drum);
			}

			/* tc */
			if ((rhy & 0x02) != 0) {
				EnvelopeKeyOn(channel8.slotz[1], egk_drum);
			} else {
				EnvelopeKeyOff(channel8.slotz[1], egk_drum);
			}

			/* tom */
			if ((rhy & 0x04) != 0) {
				EnvelopeKeyOn(channel8.slotz[0], egk_drum);
			} else {
				EnvelopeKeyOff(channel8.slotz[0], egk_drum);
			}

			/* sd */
			if ((rhy & 0x08) != 0) {
				EnvelopeKeyOn(channel7.slotz[1], egk_drum);
			} else {
				EnvelopeKeyOff(channel7.slotz[1], egk_drum);
			}

			/* bd */
			if ((rhy & 0x10) != 0) {
				EnvelopeKeyOn(channel6.slotz[0], egk_drum);
				EnvelopeKeyOn(channel6.slotz[1], egk_drum);
			} else {
				EnvelopeKeyOff(channel6.slotz[0], egk_drum);
				EnvelopeKeyOff(channel6.slotz[1], egk_drum);
			}
		} else {
			for (chnum = 6; chnum < 9; chnum++) {
				channel[chnum].chtype = ch_2op;
				ChannelSetupAlg(channel[chnum]);
				EnvelopeKeyOff(channel[chnum].slotz[0], egk_drum);
				EnvelopeKeyOff(channel[chnum].slotz[1], egk_drum);
			}
		}
	}

	private static void ChannelUpdateFrequency(Channel channel) {
		channel.ksv = (byte)((channel.block << 1)
		                     | ((channel.f_num >> (0x09 - channel.chip.nts)) & 0x01));
		EnvelopeUpdateKSL(channel.slotz[0]);
		EnvelopeUpdateKSL(channel.slotz[1]);
	}

	private static void ChannelRestoreFrequency(Channel channel) {
		channel.f_num = channel.f_num_reg;
		channel.block = channel.block_reg;
		ChannelUpdateFrequency(channel);
	}

	private static void ChannelSync4Op(Channel channel) {
		channel.pair!.f_num = channel.f_num;
		channel.pair.block = channel.block;
		ChannelUpdateFrequency(channel.pair);
	}

	private static void ChannelWriteA0(Channel channel, byte data) {
		channel.f_num_reg = (ushort)((channel.f_num_reg & 0x300) | data);
		if (channel.chtype == ch_4op2) {
			return;
		}

		ChannelRestoreFrequency(channel);
		if (channel.chtype == ch_4op) {
			ChannelSync4Op(channel);
		}
	}

	private static void ChannelWriteB0(Channel channel, byte data) {
		channel.f_num_reg = (ushort)((channel.f_num_reg & 0xff) | ((data & 0x03) << 8));
		channel.block_reg = (byte)((data >> 2) & 0x07);
		if (channel.chtype == ch_4op2) {
			return;
		}

		ChannelRestoreFrequency(channel);
		if (channel.chtype == ch_4op) {
			ChannelSync4Op(channel);
		}
	}

	private static void ChannelSetupAlg(Channel channel) {
		Signal zeromod = channel.chip.zeromod;
		if (channel.chtype == ch_drum) {
			if (channel.ch_num == 7 || channel.ch_num == 8) {
				channel.slotz[0].mod = zeromod;
				channel.slotz[1].mod = zeromod;
				return;
			}

			switch (channel.alg & 0x01) {
				case 0x00:
					channel.slotz[0].mod = channel.slotz[0].fbmod;
					channel.slotz[1].mod = channel.slotz[0].@out;
					break;
				case 0x01:
					channel.slotz[0].mod = channel.slotz[0].fbmod;
					channel.slotz[1].mod = zeromod;
					break;
			}

			return;
		}

		if ((channel.alg & 0x08) != 0) {
			return;
		}

		if ((channel.alg & 0x04) != 0) {
			Channel pair = channel.pair!;
			pair.@out[0] = zeromod;
			pair.@out[1] = zeromod;
			pair.@out[2] = zeromod;
			pair.@out[3] = zeromod;
			switch (channel.alg & 0x03) {
				case 0x00:
					pair.slotz[0].mod = pair.slotz[0].fbmod;
					pair.slotz[1].mod = pair.slotz[0].@out;
					channel.slotz[0].mod = pair.slotz[1].@out;
					channel.slotz[1].mod = channel.slotz[0].@out;
					channel.@out[0] = channel.slotz[1].@out;
					channel.@out[1] = zeromod;
					channel.@out[2] = zeromod;
					channel.@out[3] = zeromod;
					break;
				case 0x01:
					pair.slotz[0].mod = pair.slotz[0].fbmod;
					pair.slotz[1].mod = pair.slotz[0].@out;
					channel.slotz[0].mod = zeromod;
					channel.slotz[1].mod = channel.slotz[0].@out;
					channel.@out[0] = pair.slotz[1].@out;
					channel.@out[1] = channel.slotz[1].@out;
					channel.@out[2] = zeromod;
					channel.@out[3] = zeromod;
					break;
				case 0x02:
					pair.slotz[0].mod = pair.slotz[0].fbmod;
					pair.slotz[1].mod = zeromod;
					channel.slotz[0].mod = pair.slotz[1].@out;
					channel.slotz[1].mod = channel.slotz[0].@out;
					channel.@out[0] = pair.slotz[0].@out;
					channel.@out[1] = channel.slotz[1].@out;
					channel.@out[2] = zeromod;
					channel.@out[3] = zeromod;
					break;
				case 0x03:
					pair.slotz[0].mod = pair.slotz[0].fbmod;
					pair.slotz[1].mod = zeromod;
					channel.slotz[0].mod = pair.slotz[1].@out;
					channel.slotz[1].mod = zeromod;
					channel.@out[0] = pair.slotz[0].@out;
					channel.@out[1] = channel.slotz[0].@out;
					channel.@out[2] = channel.slotz[1].@out;
					channel.@out[3] = zeromod;
					break;
			}
		} else {
			switch (channel.alg & 0x01) {
				case 0x00:
					channel.slotz[0].mod = channel.slotz[0].fbmod;
					channel.slotz[1].mod = channel.slotz[0].@out;
					channel.@out[0] = channel.slotz[1].@out;
					channel.@out[1] = zeromod;
					channel.@out[2] = zeromod;
					channel.@out[3] = zeromod;
					break;
				case 0x01:
					channel.slotz[0].mod = channel.slotz[0].fbmod;
					channel.slotz[1].mod = zeromod;
					channel.@out[0] = channel.slotz[0].@out;
					channel.@out[1] = channel.slotz[1].@out;
					channel.@out[2] = zeromod;
					channel.@out[3] = zeromod;
					break;
			}
		}
	}

	private static void ChannelUpdateAlg(Channel channel) {
		channel.alg = channel.con;
		if (channel.chtype == ch_4op) {
			channel.pair!.alg = (byte)(0x04 | (channel.con << 1) | channel.pair.con);
			channel.alg = 0x08;
			ChannelSetupAlg(channel.pair);
		} else if (channel.chtype == ch_4op2) {
			channel.alg = (byte)(0x04 | (channel.pair!.con << 1) | channel.con);
			channel.pair.alg = 0x08;
			ChannelSetupAlg(channel);
		} else {
			ChannelSetupAlg(channel);
		}
	}

	private static void ChannelWriteC0(Channel channel, byte data) {
		channel.fb = (byte)((data & 0x0e) >> 1);
		channel.con = (byte)(data & 0x01);
		ChannelUpdateAlg(channel);
		if (channel.chip.newm != 0) {
			channel.cha = (ushort)(((data >> 4) & 0x01) != 0 ? 0xffff : 0);
			channel.chb = (ushort)(((data >> 5) & 0x01) != 0 ? 0xffff : 0);
			channel.chc = (ushort)(((data >> 6) & 0x01) != 0 ? 0xffff : 0);
			channel.chd = (ushort)(((data >> 7) & 0x01) != 0 ? 0xffff : 0);
		} else {
			channel.cha = channel.chb = 0xffff;
			channel.chc = channel.chd = 0;
		}
	}

	private static void ChannelKeyOn(Channel channel) {
		if (channel.chtype == ch_4op) {
			EnvelopeKeyOn(channel.slotz[0], egk_norm);
			EnvelopeKeyOn(channel.slotz[1], egk_norm);
			EnvelopeKeyOn(channel.pair!.slotz[0], egk_norm);
			EnvelopeKeyOn(channel.pair.slotz[1], egk_norm);
		} else if (channel.chtype == ch_2op || channel.chtype == ch_drum) {
			EnvelopeKeyOn(channel.slotz[0], egk_norm);
			EnvelopeKeyOn(channel.slotz[1], egk_norm);
		}
	}

	private static void ChannelKeyOff(Channel channel) {
		if (channel.chtype == ch_4op) {
			EnvelopeKeyOff(channel.slotz[0], egk_norm);
			EnvelopeKeyOff(channel.slotz[1], egk_norm);
			EnvelopeKeyOff(channel.pair!.slotz[0], egk_norm);
			EnvelopeKeyOff(channel.pair.slotz[1], egk_norm);
		} else if (channel.chtype == ch_2op || channel.chtype == ch_drum) {
			EnvelopeKeyOff(channel.slotz[0], egk_norm);
			EnvelopeKeyOff(channel.slotz[1], egk_norm);
		}
	}

	private void ChannelSet4Op(byte data) {
		byte bit;
		byte chnum;
		for (bit = 0; bit < 6; bit++) {
			chnum = bit;
			if (bit >= 3) {
				chnum += 9 - 3;
			}

			if (((data >> bit) & 0x01) != 0) {
				channel[chnum].chtype = ch_4op;
				channel[chnum + 3].chtype = ch_4op2;
				ChannelSync4Op(channel[chnum]);
				ChannelUpdateAlg(channel[chnum]);
			} else {
				channel[chnum].chtype = ch_2op;
				channel[chnum + 3].chtype = ch_2op;
				ChannelRestoreFrequency(channel[chnum + 3]);
				ChannelUpdateAlg(channel[chnum]);
				ChannelUpdateAlg(channel[chnum + 3]);
			}
		}
	}

	private static short ClipSample(int sample) {
		if (sample > 32767) {
			sample = 32767;
		} else if (sample < -32768) {
			sample = -32768;
		}

		return (short)sample;
	}

	private static void ProcessSlot(Slot slot) {
		SlotCalcFB(slot);
		EnvelopeCalc(slot);
		PhaseGenerate(slot);
		SlotGenerate(slot);
	}

	/// <summary>
	/// <c>OPL3_Generate4Ch</c>: one sample at <see cref="NativeRate"/> on the chip's four outputs, A, B, C and D, into
	/// <paramref name="buf4"/>. The FM channels' right side (B, D) is the sample before the left's
	/// (<c>OPL_QUIRK_CHANNELSAMPLEDELAY</c>).
	/// </summary>
	public void Generate4Ch(Span<short> buf4) {
		Channel channel;
		Signal[] @out;
		Span<int> mix = stackalloc int[2];
		byte ii;
		short accm;
		byte shift = 0;

		buf4[1] = ClipSample(mixbuff[1]);
		buf4[3] = ClipSample(mixbuff[3]);

		for (ii = 0; ii < 15; ii++) {
			ProcessSlot(slot[ii]);
		}

		mix[0] = mix[1] = 0;
		for (ii = 0; ii < 18; ii++) {
			channel = this.channel[ii];
			@out = channel.@out;
			accm = (short)(@out[0].value + @out[1].value + @out[2].value + @out[3].value);
			mix[0] += (short)(accm & channel.cha);
			mix[1] += (short)(accm & channel.chc);
		}

		mixbuff[0] = mix[0];
		mixbuff[2] = mix[1];

		for (ii = 15; ii < 18; ii++) {
			ProcessSlot(slot[ii]);
		}

		buf4[0] = ClipSample(mixbuff[0]);
		buf4[2] = ClipSample(mixbuff[2]);

		for (ii = 18; ii < 33; ii++) {
			ProcessSlot(slot[ii]);
		}

		mix[0] = mix[1] = 0;
		for (ii = 0; ii < 18; ii++) {
			channel = this.channel[ii];
			@out = channel.@out;
			accm = (short)(@out[0].value + @out[1].value + @out[2].value + @out[3].value);
			mix[0] += (short)(accm & channel.chb);
			mix[1] += (short)(accm & channel.chd);
		}

		mixbuff[1] = mix[0];
		mixbuff[3] = mix[1];

		for (ii = 33; ii < 36; ii++) {
			ProcessSlot(slot[ii]);
		}

		if ((timer & 0x3f) == 0x3f) {
			tremolopos = (byte)((tremolopos + 1) % 210);
		}

		if (tremolopos < 105) {
			tremolo = (byte)(tremolopos >> tremoloshift);
		} else {
			tremolo = (byte)((210 - tremolopos) >> tremoloshift);
		}

		if ((timer & 0x3ff) == 0x3ff) {
			vibpos = (byte)((vibpos + 1) & 7);
		}

		timer++;

		if (eg_state != 0) {
			while (shift < 13 && ((eg_timer >> shift) & 1) == 0) {
				shift++;
			}

			if (shift > 12) {
				eg_add = 0;
			} else {
				eg_add = (byte)(shift + 1);
			}

			eg_timer_lo = (byte)(eg_timer & 0x3u);
		}

		if (eg_timerrem != 0 || eg_state != 0) {
			if (eg_timer == 0xfffffffffUL) {
				eg_timer = 0;
				eg_timerrem = 1;
			} else {
				eg_timer++;
				eg_timerrem = 0;
			}
		}

		eg_state ^= 1;

		while (writebuf[writebuf_cur].time <= writebuf_samplecnt) {
			ref WriteBuf entry = ref writebuf[writebuf_cur];
			if ((entry.reg & 0x200) == 0) {
				break;
			}

			entry.reg &= 0x1ff;
			WriteReg(entry.reg, entry.data);
			writebuf_cur = (writebuf_cur + 1) % OPL_WRITEBUF_SIZE;
		}

		writebuf_samplecnt++;
	}

	/// <summary><c>OPL3_Generate</c>: one stereo sample at <see cref="NativeRate"/>, the chip's outputs A and B, into <paramref name="buf"/>.</summary>
	public void Generate(Span<short> buf) {
		Span<short> samples = stackalloc short[4];
		Generate4Ch(samples);
		buf[0] = samples[0];
		buf[1] = samples[1];
	}

	/// <summary><c>OPL3_Generate4ChResampled</c>: <see cref="Generate4Ch"/> converted to the rate <see cref="Reset"/> was given, by linear interpolation.</summary>
	public void Generate4ChResampled(Span<short> buf4) {
		while (samplecnt >= rateratio) {
			oldsamples[0] = samples[0];
			oldsamples[1] = samples[1];
			oldsamples[2] = samples[2];
			oldsamples[3] = samples[3];
			Generate4Ch(samples);
			samplecnt -= rateratio;
		}

		buf4[0] = (short)((oldsamples[0] * (rateratio - samplecnt)
		                   + samples[0] * samplecnt) / rateratio);
		buf4[1] = (short)((oldsamples[1] * (rateratio - samplecnt)
		                   + samples[1] * samplecnt) / rateratio);
		buf4[2] = (short)((oldsamples[2] * (rateratio - samplecnt)
		                   + samples[2] * samplecnt) / rateratio);
		buf4[3] = (short)((oldsamples[3] * (rateratio - samplecnt)
		                   + samples[3] * samplecnt) / rateratio);
		samplecnt += 1 << RSM_FRAC;
	}

	/// <summary><c>OPL3_GenerateResampled</c>: <see cref="Generate"/> converted to the rate <see cref="Reset"/> was given.</summary>
	public void GenerateResampled(Span<short> buf) {
		Span<short> samples = stackalloc short[4];
		Generate4ChResampled(samples);
		buf[0] = samples[0];
		buf[1] = samples[1];
	}

	/// <summary><c>OPL3_Reset</c>: every register and every internal state cleared, the resampled output set to <paramref name="samplerate"/>.</summary>
	public void Reset(uint samplerate) {
		Slot slot;
		Channel channel;
		byte slotnum;
		byte channum;
		byte local_ch_slot;

		/* memset(chip, 0, sizeof(opl3_chip)) */
		timer = 0;
		eg_timer = 0;
		eg_timerrem = eg_state = eg_add = eg_timer_lo = 0;
		newm = nts = rhy = vibpos = vibshift = tremolo = tremolopos = tremoloshift = 0;
		noise = 0;
		zeromod.value = 0;
		Array.Clear(mixbuff);
		rm_hh_bit2 = rm_hh_bit3 = rm_hh_bit7 = rm_hh_bit8 = rm_tc_bit3 = rm_tc_bit5 = 0;
		rateratio = samplecnt = 0;
		Array.Clear(oldsamples);
		Array.Clear(samples);
		writebuf_samplecnt = 0;
		writebuf_cur = writebuf_last = 0;
		writebuf_lasttime = 0;
		Array.Clear(writebuf);

		for (slotnum = 0; slotnum < 36; slotnum++) {
			slot = this.slot[slotnum] = new Slot();
			slot.chip = this;
			slot.mod = zeromod;
			slot.eg_rout = 0x1ff;
			slot.eg_out = 0x1ff;
			slot.eg_gen = envelope_gen_num_release;
			slot.trem_on = false;
			slot.slot_num = slotnum;
		}

		for (channum = 0; channum < 18; channum++) {
			this.channel[channum] = new Channel();
		}

		for (channum = 0; channum < 18; channum++) {
			channel = this.channel[channum];
			local_ch_slot = ch_slot[channum];
			channel.slotz[0] = this.slot[local_ch_slot];
			channel.slotz[1] = this.slot[local_ch_slot + 3];
			this.slot[local_ch_slot].channel = channel;
			this.slot[local_ch_slot + 3].channel = channel;
			if ((channum % 9) < 3) {
				channel.pair = this.channel[channum + 3];
			} else if ((channum % 9) < 6) {
				channel.pair = this.channel[channum - 3];
			}

			channel.chip = this;
			channel.@out[0] = zeromod;
			channel.@out[1] = zeromod;
			channel.@out[2] = zeromod;
			channel.@out[3] = zeromod;
			channel.chtype = ch_2op;
			channel.cha = 0xffff;
			channel.chb = 0xffff;
			channel.ch_num = channum;
			ChannelSetupAlg(channel);
		}

		noise = 1;
		rateratio = (int)((samplerate << RSM_FRAC) / NativeRate);
		tremoloshift = 4;
		vibshift = 1;
	}

	/// <summary><c>OPL3_WriteReg</c>: <paramref name="v"/> written to register <paramref name="reg"/> at once, the second register array at <c>0x100</c>–<c>0x1ff</c>.</summary>
	public void WriteReg(ushort reg, byte v) {
		byte high = (byte)((reg >> 8) & 0x01);
		byte regm = (byte)(reg & 0xff);
		switch (regm & 0xf0) {
			case 0x00:
				if (high != 0) {
					switch (regm & 0x0f) {
						case 0x04:
							ChannelSet4Op(v);
							break;
						case 0x05:
							newm = (byte)(v & 0x01);
							break;
					}
				} else {
					switch (regm & 0x0f) {
						case 0x08:
							nts = (byte)((v >> 6) & 0x01);
							break;
					}
				}

				break;
			case 0x20:
			case 0x30:
				if (ad_slot[regm & 0x1fu] >= 0) {
					SlotWrite20(slot[18 * high + ad_slot[regm & 0x1fu]], v);
				}

				break;
			case 0x40:
			case 0x50:
				if (ad_slot[regm & 0x1fu] >= 0) {
					SlotWrite40(slot[18 * high + ad_slot[regm & 0x1fu]], v);
				}

				break;
			case 0x60:
			case 0x70:
				if (ad_slot[regm & 0x1fu] >= 0) {
					SlotWrite60(slot[18 * high + ad_slot[regm & 0x1fu]], v);
				}

				break;
			case 0x80:
			case 0x90:
				if (ad_slot[regm & 0x1fu] >= 0) {
					SlotWrite80(slot[18 * high + ad_slot[regm & 0x1fu]], v);
				}

				break;
			case 0xe0:
			case 0xf0:
				if (ad_slot[regm & 0x1fu] >= 0) {
					SlotWriteE0(slot[18 * high + ad_slot[regm & 0x1fu]], v);
				}

				break;
			case 0xa0:
				if ((regm & 0x0f) < 9) {
					ChannelWriteA0(channel[9 * high + (regm & 0x0f)], v);
				}

				break;
			case 0xb0:
				if (regm == 0xbd && high == 0) {
					tremoloshift = (byte)((((v >> 7) ^ 1) << 1) + 2);
					vibshift = (byte)(((v >> 6) & 0x01) ^ 1);
					ChannelUpdateRhythm(v);
				} else if ((regm & 0x0f) < 9) {
					ChannelWriteB0(channel[9 * high + (regm & 0x0f)], v);
					if ((v & 0x20) != 0) {
						ChannelKeyOn(channel[9 * high + (regm & 0x0f)]);
					} else {
						ChannelKeyOff(channel[9 * high + (regm & 0x0f)]);
					}
				}

				break;
			case 0xc0:
				if ((regm & 0x0f) < 9) {
					ChannelWriteC0(channel[9 * high + (regm & 0x0f)], v);
				}

				break;
		}
	}

	/// <summary>
	/// <c>OPL3_WriteRegBuffered</c>: <paramref name="v"/> queued for register <paramref name="reg"/>, applied by a later
	/// <see cref="Generate4Ch"/> at least <c>OPL_WRITEBUF_DELAY</c> samples after the write queued before it.
	/// </summary>
	public void WriteRegBuffered(ushort reg, byte v) {
		ulong time1, time2;
		uint writebuf_last;

		writebuf_last = this.writebuf_last;
		ref WriteBuf writebuf = ref this.writebuf[writebuf_last];

		if ((writebuf.reg & 0x200) != 0) {
			WriteReg((ushort)(writebuf.reg & 0x1ff), writebuf.data);

			writebuf_cur = (writebuf_last + 1) % OPL_WRITEBUF_SIZE;
			writebuf_samplecnt = writebuf.time;
		}

		writebuf.reg = (ushort)(reg | 0x200);
		writebuf.data = v;
		time1 = writebuf_lasttime + OPL_WRITEBUF_DELAY;
		time2 = writebuf_samplecnt;

		if (time1 < time2) {
			time1 = time2;
		}

		writebuf.time = time1;
		writebuf_lasttime = time1;
		this.writebuf_last = (writebuf_last + 1) % OPL_WRITEBUF_SIZE;
	}

	/// <summary>
	/// <c>OPL3_Generate4ChStream</c>: <see cref="Generate4ChResampled"/> for each frame, outputs A and B interleaved into
	/// <paramref name="sndptr1"/>, C and D into <paramref name="sndptr2"/>; <paramref name="numsamples"/> frames.
	/// </summary>
	public void Generate4ChStream(Span<short> sndptr1, Span<short> sndptr2, uint numsamples) {
		Span<short> samples = stackalloc short[4];

		for (int i = 0; i < numsamples; i++) {
			Generate4ChResampled(samples);
			sndptr1[2 * i] = samples[0];
			sndptr1[2 * i + 1] = samples[1];
			sndptr2[2 * i] = samples[2];
			sndptr2[2 * i + 1] = samples[3];
		}
	}

	/// <summary><c>OPL3_GenerateStream</c>: <see cref="GenerateResampled"/> for each frame, interleaved into <paramref name="sndptr"/>; <paramref name="numsamples"/> frames.</summary>
	public void GenerateStream(Span<short> sndptr, uint numsamples) {
		for (int i = 0; i < numsamples; i++) {
			GenerateResampled(sndptr.Slice(2 * i, 2));
		}
	}
}
