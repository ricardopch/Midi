// Generado desde el motor Java con tools/java2cs.py. No editar a mano.
using System;
using System.Collections.Generic;
using System.IO;

namespace MelodiaMidi.Engine
{
/**
 * Codificador MP3 propio (MPEG-1 Layer III, mono, tasa de bits constante).
 * Banco de filtros polifásico + MDCT de bloques largos, cuantización con búsqueda de la ganancia global
 * y codificación Huffman con las tablas de la norma ISO/IEC 11172-3. Sin reserva de bits ni factores de escala:
 * sencillo y robusto, con calidad de sobra para el audio sintetizado de la app.
 */
public sealed class Mp3Encoder {
    private const int GRANULE = 576;
    private static readonly int[] BITRATES = {0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320};
    private static readonly int[] SFB_44 = {0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 52, 62, 74, 90, 110, 134, 162, 196, 238, 288, 342, 418, 576};
    private static readonly int[] SFB_48 = {0, 4, 8, 12, 16, 20, 24, 30, 36, 42, 50, 60, 72, 88, 106, 128, 156, 190, 230, 276, 330, 384, 576};
    private static readonly int[] SFB_32 = {0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 54, 66, 82, 102, 126, 156, 194, 240, 296, 364, 448, 550, 576};
    /** Reparto habitual de las tres regiones de "big values" según cuántas bandas ocupan (región 0, región 1). */
    private static readonly int[] SUBDV_R0 = {0, 0, 0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6};
    private static readonly int[] SUBDV_R1 = {0, 0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 3, 4, 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 7};
    private static readonly int[] LINBITS = {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13};
    private static readonly int[] XLEN = {0, 2, 3, 3, 0, 4, 4, 6, 6, 6, 8, 8, 8, 16, 0, 16};
    private static readonly int[] TABLE_IDS = {1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 24, 32, 33};
    /** Códigos Huffman de la norma (ISO 11172-3, tablas B.7): longitud:código, en orden x*ylen+y. */
    private static readonly string[] HUFF = {
            "1:1,3:1,2:1,3:0",
            "1:1,3:2,6:1,3:3,3:1,5:1,5:3,5:2,6:0",
            "2:3,2:2,6:1,3:1,2:1,5:1,5:3,5:2,6:0",
            "1:1,3:2,6:6,7:5,3:3,3:1,6:4,7:4,6:7,6:5,7:7,8:1,7:6,6:1,7:1,8:0",
            "3:7,3:3,5:5,7:1,3:6,2:2,4:3,5:2,4:5,4:4,5:4,6:1,6:3,5:3,6:2,7:0",
            "1:1,3:2,6:10,8:19,8:16,9:10,3:3,4:3,6:7,7:10,7:5,8:3,6:11,5:4,7:13,8:17,8:8,9:4,7:12,7:11,8:18,9:15,9:11,9:2,7:7,7:6,8:9,9:14,9:3,10:1,8:6,8:4,9:5,10:3,10:2,10:0",
            "2:3,3:4,6:6,8:18,8:12,9:5,3:5,2:1,4:2,8:16,8:9,8:3,6:7,4:3,6:5,8:14,8:7,9:3,8:19,8:17,8:15,9:13,9:10,10:4,8:13,7:5,8:8,9:11,10:5,10:1,9:12,8:4,9:4,9:1,11:1,11:0",
            "3:7,3:5,5:9,6:14,8:15,9:7,3:6,3:4,4:5,5:5,6:6,8:7,4:7,4:6,5:8,6:8,7:8,8:5,6:15,5:6,6:9,7:10,7:5,8:1,7:11,6:7,7:9,7:6,8:4,9:1,8:14,7:4,8:6,8:2,9:6,9:0",
            "1:1,3:2,6:10,8:23,9:35,9:30,9:12,10:17,3:3,4:3,6:8,7:12,8:18,9:21,8:12,8:7,6:11,6:9,7:15,8:21,9:32,10:40,9:19,9:6,7:14,7:13,8:22,9:34,10:46,10:23,9:18,10:7,8:20,8:19,9:33,10:47,10:27,10:22,10:9,10:3,9:31,9:22,10:41,10:26,11:21,11:20,10:5,11:3,8:14,8:13,9:10,10:11,10:16,10:6,11:5,11:1,9:9,8:8,9:7,10:8,10:4,11:4,11:2,11:0",
            "2:3,3:4,5:10,7:24,8:34,9:33,8:21,9:15,3:5,3:3,4:4,6:10,8:32,8:17,7:11,8:10,5:11,5:7,6:13,7:18,8:30,9:31,8:20,8:5,7:25,6:11,7:19,9:59,8:27,10:18,8:12,9:5,8:35,8:33,8:31,9:58,9:30,10:16,9:7,10:5,8:28,8:26,9:32,10:19,10:17,11:15,10:8,11:14,8:14,7:12,7:9,8:13,9:14,10:9,10:4,10:1,8:11,7:4,8:6,9:6,10:6,10:3,10:2,10:0",
            "4:9,3:6,5:16,7:33,8:41,9:39,9:38,9:26,3:7,3:5,4:6,5:9,7:23,7:16,8:26,8:11,5:17,4:7,5:11,6:14,7:21,8:30,7:10,8:7,6:17,5:10,6:15,6:12,7:18,8:28,8:14,8:5,7:32,6:13,7:22,7:19,8:18,8:16,8:9,9:5,8:40,7:17,8:31,8:29,8:17,9:13,8:4,9:2,8:27,7:12,7:11,8:15,8:10,9:7,9:4,10:1,9:27,8:12,8:8,9:12,9:6,9:3,9:1,10:0",
            "1:1,4:5,6:14,7:21,8:34,9:51,9:46,10:71,9:42,10:52,11:68,11:52,12:67,12:44,13:43,13:19,3:3,4:4,6:12,7:19,8:31,8:26,9:44,9:33,9:31,9:24,10:32,10:24,11:31,12:35,12:22,12:14,6:15,6:13,7:23,8:36,9:59,9:49,10:77,10:65,9:29,10:40,10:30,11:40,11:27,12:33,13:42,13:16,7:22,7:20,8:37,9:61,9:56,10:79,10:73,10:64,10:43,11:76,11:56,11:37,11:26,12:31,13:25,13:14,8:35,7:16,9:60,9:57,10:97,10:75,11:114,11:91,10:54,11:73,11:55,12:41,12:48,13:53,13:23,14:24,9:58,8:27,9:50,10:96,10:76,10:70,11:93,11:84,11:77,11:58,12:79,11:29,13:74,13:49,14:41,14:17,9:47,9:45,10:78,10:74,11:115,11:94,11:90,11:79,11:69,12:83,12:71,12:50,13:59,13:38,14:36,14:15,10:72,9:34,10:56,11:95,11:92,11:85,12:91,12:90,12:86,12:73,13:77,13:65,13:51,14:44,16:43,16:42,9:43,8:20,9:30,10:44,10:55,11:78,11:72,12:87,12:78,12:61,12:46,13:54,13:37,14:30,15:20,15:16,10:53,9:25,10:41,10:37,11:44,11:59,11:54,13:81,12:66,13:76,13:57,14:54,14:37,14:18,16:39,15:11,10:35,10:33,10:31,11:57,11:42,12:82,12:72,13:80,12:47,13:58,14:55,13:21,14:22,15:26,16:38,17:22,11:53,10:25,10:23,11:38,12:70,12:60,12:51,12:36,13:55,13:26,13:34,14:23,15:27,15:14,15:9,16:7,11:34,11:32,11:28,12:39,12:49,13:75,12:30,13:52,14:48,14:40,15:52,15:28,15:18,16:17,16:9,16:5,12:45,11:21,12:34,13:64,13:56,13:50,14:49,14:45,14:31,14:19,14:12,15:15,16:10,15:7,16:6,16:3,13:48,12:23,12:20,13:39,13:36,13:35,15:53,14:21,14:16,17:23,15:13,15:10,15:6,17:1,16:4,16:2,12:16,12:15,13:17,14:27,14:25,14:20,15:29,14:11,15:17,15:12,16:16,16:8,19:1,18:1,19:0,16:1",
            "3:7,4:12,5:18,7:53,7:47,8:76,9:124,9:108,9:89,10:123,10:108,11:119,11:107,11:81,12:122,13:63,4:13,3:5,5:16,6:27,7:46,7:36,8:61,8:51,8:42,9:70,9:52,10:83,10:65,10:41,11:59,11:36,5:19,5:17,5:15,6:24,7:41,7:34,8:59,8:48,8:40,9:64,9:50,10:78,10:62,11:80,11:56,11:33,6:29,6:28,6:25,7:43,7:39,8:63,8:55,9:93,9:76,9:59,10:93,10:72,10:54,11:75,11:50,11:29,7:52,6:22,7:42,7:40,8:67,8:57,9:95,9:79,9:72,9:57,10:89,10:69,10:49,11:66,11:46,11:27,8:77,7:37,7:35,8:66,8:58,8:52,9:91,9:74,9:62,9:48,10:79,10:63,11:90,11:62,11:40,12:38,9:125,7:32,8:60,8:56,8:50,9:92,9:78,9:65,9:55,10:87,10:71,10:51,11:73,11:51,12:70,12:30,9:109,8:53,8:49,9:94,9:88,9:75,9:66,10:122,10:91,10:73,10:56,10:42,11:64,11:44,11:21,12:25,9:90,8:43,8:41,9:77,9:73,9:63,9:56,10:92,10:77,10:66,10:47,11:67,11:48,12:53,12:36,12:20,9:71,8:34,9:67,9:60,9:58,9:49,10:88,10:76,10:67,11:106,11:71,11:54,11:38,12:39,12:23,12:15,10:109,9:53,9:51,9:47,10:90,10:82,10:58,10:57,10:48,11:72,11:57,11:41,11:23,12:27,13:62,12:9,10:86,9:42,9:40,9:37,10:70,10:64,10:52,10:43,11:70,11:55,11:42,11:25,12:29,12:18,12:11,13:11,11:118,10:68,9:30,10:55,10:50,10:46,11:74,11:65,11:49,11:39,11:24,11:16,12:22,12:13,13:14,13:7,11:91,10:44,10:39,10:38,10:34,11:63,11:52,11:45,11:31,12:52,12:28,12:19,12:14,12:8,13:9,13:3,12:123,11:60,11:58,11:53,11:47,11:43,11:32,11:22,12:37,12:24,12:17,12:12,13:15,13:10,12:2,13:1,12:71,11:37,11:34,11:30,11:28,11:20,11:17,12:26,12:21,12:16,12:10,12:6,13:8,13:6,13:2,13:0",
            "1:1,4:5,6:14,8:44,9:74,9:63,10:110,10:93,11:172,11:149,11:138,12:242,12:225,12:195,13:376,9:17,3:3,4:4,6:12,7:20,8:35,9:62,9:53,9:47,10:83,10:75,10:68,11:119,12:201,11:107,12:207,8:9,6:15,6:13,7:23,8:38,9:67,9:58,10:103,10:90,11:161,10:72,11:127,11:117,11:110,12:209,12:206,9:16,8:45,7:21,8:39,9:69,9:64,10:114,10:99,10:87,11:158,11:140,12:252,12:212,12:199,13:387,13:365,10:26,9:75,8:36,9:68,9:65,10:115,10:101,11:179,11:164,11:155,12:264,12:246,12:226,13:395,13:382,13:362,9:9,9:66,8:30,9:59,9:56,10:102,11:185,11:173,12:265,11:142,12:253,12:232,13:400,13:388,13:378,14:445,10:16,10:111,9:54,9:52,10:100,11:184,11:178,11:160,11:133,12:257,12:244,12:228,12:217,13:385,13:366,14:715,10:10,10:98,9:48,10:91,10:88,11:165,11:157,11:148,12:261,12:248,13:407,13:397,13:372,13:380,15:889,15:884,10:8,10:85,10:84,10:81,11:159,11:156,11:143,12:260,12:249,13:427,13:401,13:392,13:383,14:727,14:713,14:708,10:7,11:154,10:76,10:73,11:141,11:131,12:256,12:245,13:426,13:406,13:394,13:384,14:735,13:359,14:710,13:352,11:11,11:139,11:129,10:67,11:125,12:247,12:233,12:229,12:219,13:393,14:743,14:737,14:720,15:885,15:882,14:439,10:4,12:243,11:120,11:118,11:115,12:227,12:223,13:396,14:746,14:742,14:736,14:721,14:712,14:706,13:223,14:436,11:6,12:202,12:224,12:222,12:218,12:216,13:389,13:386,13:381,13:364,15:888,14:443,14:707,14:440,14:437,16:1728,11:4,14:747,12:211,12:210,12:208,13:370,13:379,14:734,14:723,14:714,16:1735,15:883,15:877,15:876,17:3459,15:865,11:2,13:377,13:369,11:102,12:187,14:726,14:722,13:358,14:711,14:709,15:866,16:1734,15:871,17:3458,15:870,14:434,11:0,9:12,8:10,8:7,9:11,9:10,10:17,10:11,10:9,11:13,11:12,11:10,11:7,11:5,11:3,11:1,8:3",
            "4:15,4:13,6:46,7:80,8:146,9:262,9:248,10:434,10:426,11:669,11:653,11:649,11:621,11:517,12:1032,9:88,4:14,4:12,5:21,6:38,7:71,8:130,8:122,9:216,9:209,9:198,10:327,10:345,10:319,10:297,10:279,8:42,6:47,5:22,6:41,7:74,7:68,8:128,8:120,9:221,9:207,9:194,9:182,10:340,10:315,10:295,11:541,7:18,7:81,6:39,7:75,7:70,8:134,8:125,8:116,9:220,9:204,9:190,9:178,10:325,10:311,10:293,10:271,7:16,8:147,7:72,7:69,8:135,8:127,8:118,8:112,9:210,9:200,9:188,10:352,10:323,10:306,10:285,11:540,7:14,9:263,7:66,8:129,8:126,8:119,8:114,9:214,9:202,9:192,9:180,10:341,10:317,10:301,10:281,10:262,7:12,9:249,8:123,8:121,8:117,8:113,9:215,9:206,9:195,9:185,10:347,10:330,10:308,10:291,10:272,11:520,7:10,10:435,8:115,8:111,8:109,9:211,9:203,9:196,9:187,10:353,10:332,10:313,10:298,10:283,11:531,11:381,8:17,10:427,9:212,9:208,9:205,9:201,9:193,9:186,9:177,9:169,10:320,10:303,10:286,10:268,11:514,11:377,8:16,10:335,9:199,9:197,9:191,9:189,9:181,9:174,10:333,10:321,10:305,10:289,10:275,11:521,11:379,11:371,8:11,11:668,9:184,9:183,9:179,9:175,10:344,10:331,10:314,10:304,10:290,10:277,11:530,11:383,11:373,11:366,8:10,11:652,10:346,9:171,9:168,9:164,10:318,10:309,10:299,10:287,10:276,10:263,11:513,11:375,11:368,11:362,8:6,11:648,10:322,10:316,10:312,10:307,10:302,10:292,10:284,10:269,10:261,11:512,11:376,11:370,11:364,11:359,8:4,11:620,10:300,10:296,10:294,10:288,10:282,10:273,10:266,11:515,11:380,11:374,11:369,11:365,11:361,11:357,8:2,12:1033,10:280,10:278,10:274,10:267,10:264,10:259,11:382,11:378,11:372,11:367,11:363,11:360,11:358,11:356,8:0,8:43,7:20,7:19,7:17,7:15,7:13,7:11,7:9,7:7,7:6,7:4,8:7,8:5,8:3,8:1,4:3",
            "1:1,4:5,4:4,5:5,4:6,6:5,5:4,6:4,4:7,5:3,5:6,6:0,5:7,6:2,6:3,6:1",
            "4:15,4:14,4:13,4:12,4:11,4:10,4:9,4:8,4:7,4:6,4:5,4:4,4:3,4:2,4:1,4:0",
    };
    /** Ventana de análisis C[i] de la norma (tabla C.1), en millonésimas. */
    private static readonly string WINDOW =
            "0,0,0,0,0,0,0,-1,-1,-1,-1,-1,-1,-2,-2,-2,-2,-3,-3,-3,-4,-4,-5,-5,-6,-7,-8,-8,-9,-10,-11,-12,-14,-15,"
            + "-17,-18,-20,-21,-23,-25,-28,-30,-32,-35,-38,-41,-43,-46,-50,-53,-56,-60,-63,-66,-70,-73,-77,-81,-84,"
            + "-87,-91,-93,-96,-99,102,104,106,107,108,109,109,108,107,105,103,99,95,90,84,78,70,61,51,40,27,14,-1,"
            + "-17,-34,-53,-73,-94,-116,-140,-165,-191,-219,-247,-277,-308,-339,-371,-404,-438,-473,-507,-542,-577,"
            + "-612,-647,-681,-714,-747,-779,-810,-839,-866,-892,-915,-936,-954,-969,-981,-989,-994,-995,-992,-984,"
            + "971,954,931,903,869,829,784,732,674,610,539,463,379,288,192,88,-21,-137,-260,-388,-522,-662,-807,-95"
            + "7,-1111,-1270,-1432,-1598,-1767,-1937,-2110,-2283,-2457,-2631,-2803,-2974,-3142,-3307,-3467,-3623,-3"
            + "772,-3914,-4049,-4175,-4291,-4396,-4490,-4570,-4638,-4691,-4728,-4749,-4752,-4737,-4703,-4649,-4574,"
            + "-4477,-4358,-4215,-4049,-3859,-3643,-3402,3135,2841,2522,2175,1801,1400,971,516,33,-476,-1012,-1574,"
            + "-2162,-2774,-3411,-4072,-4756,-5462,-6189,-6937,-7703,-8487,-9288,-10104,-10933,-11775,-12628,-13489"
            + ",-14359,-15234,-16113,-16994,-17876,-18757,-19634,-20507,-21372,-22229,-23074,-23907,-24725,-25527,-"
            + "26311,-27074,-27815,-28533,-29225,-29890,-30527,-31133,-31707,-32248,-32755,-33226,-33660,-34056,-34"
            + "413,-34730,-35007,-35242,-35435,-35586,-35694,-35759,35781,35759,35694,35586,35435,35242,35007,34730"
            + ",34413,34056,33660,33226,32755,32248,31707,31133,30527,29890,29225,28533,27815,27074,26311,25527,247"
            + "25,23907,23074,22229,21372,20507,19634,18757,17876,16994,16113,15234,14359,13489,12628,11775,10933,1"
            + "0104,9288,8487,7703,6937,6189,5462,4756,4072,3411,2774,2162,1574,1012,476,-33,-516,-971,-1400,-1801,"
            + "-2175,-2522,-2841,3135,3402,3643,3859,4049,4215,4358,4477,4574,4649,4703,4737,4752,4749,4728,4691,46"
            + "38,4570,4490,4396,4291,4175,4049,3914,3772,3623,3467,3307,3142,2974,2803,2631,2457,2283,2110,1937,17"
            + "67,1598,1432,1270,1111,957,807,662,522,388,260,137,21,-88,-192,-288,-379,-463,-539,-610,-674,-732,-7"
            + "84,-829,-869,-903,-931,-954,971,984,992,995,994,989,981,969,954,936,915,892,866,839,810,779,747,714,"
            + "681,647,612,577,542,507,473,438,404,371,339,308,277,247,219,191,165,140,116,94,73,53,34,17,1,-14,-27"
            + ",-40,-51,-61,-70,-78,-84,-90,-95,-99,-103,-105,-107,-108,-109,-109,-108,-107,-106,-104,102,99,96,93,"
            + "91,87,84,81,77,73,70,66,63,60,56,53,50,46,43,41,38,35,32,30,28,25,23,21,20,18,17,15,14,12,11,10,9,8,"
            + "8,7,6,5,5,4,4,3,3,3,2,2,2,2,1,1,1,1,1,1,0,0,0,0,0,0";

    private const int MAX_IX = 8206;          // 15 + 8191 (tabla con 13 bits extra)
    private const double MDCT_SCALE = 1.0 / 9;    // la MDCT directa + inversa ganan n/4 = 9

    private readonly int sr, srIndex, brIndex, kbps;
    private readonly int[] sfb;
    private readonly int[][] hcode = new int[34][];
    private readonly int[][] hlen = new int[34][];
    private readonly float[] win = new float[512];
    private readonly float[] mat = new float[32 * 64];
    private readonly float[] mdctTab = new float[18 * 36];
    private readonly float[] cs = new float[8], ca = new float[8];

    // análisis
    private readonly float[] fifo = new float[512];
    private int fifoOff;
    private readonly float[] y64 = new float[64];

    // salida
    private byte[] buf;
    private int len, acc, nbits;

    // datos de cada gránulo: ganancia, regiones, tablas…
    private const int G_PART23 = 0, G_BIG = 1, G_GAIN = 2, G_T0 = 3, G_T1 = 4, G_T2 = 5, G_R0 = 6, G_R1 = 7,
            G_C1SEL = 8, G_A1 = 9, G_A2 = 10, G_C1END = 11, G_FIELDS = 12;

    /**
     * Codifica audio mono (valores -1..1) a MP3. sampleRate: 32000, 44100 o 48000; kbps: 32..320.
     */
    public static byte[] encode(float[] pcm, int length, int sampleRate, int kbps, IProgress p) {
        if (p == null) p = ProgressUtil.NONE;
        Mp3Encoder e = new Mp3Encoder(sampleRate, kbps);
        return e.run(pcm, length, p);
    }

    private Mp3Encoder(int sampleRate, int bitrate) {
        sr = sampleRate;
        if (sampleRate == 44100) { srIndex = 0; sfb = SFB_44; }
        else if (sampleRate == 48000) { srIndex = 1; sfb = SFB_48; }
        else if (sampleRate == 32000) { srIndex = 2; sfb = SFB_32; }
        else throw new ArgumentException("Frecuencia no soportada para MP3: " + sampleRate);
        int bi = -1;
        for (int i = 1; i < BITRATES.Length; i++) if (BITRATES[i] == bitrate) bi = i;
        if (bi < 0) throw new ArgumentException("Tasa de bits no soportada: " + bitrate);
        brIndex = bi;
        kbps = bitrate;
        for (int k = 0; k < TABLE_IDS.Length; k++) {
            int[] v = parseInts(HUFF[k]);
            int n = v.Length / 2;
            int[] l = new int[n], c = new int[n];
            for (int i = 0; i < n; i++) { l[i] = v[2 * i]; c[i] = v[2 * i + 1]; }
            hlen[TABLE_IDS[k]] = l;
            hcode[TABLE_IDS[k]] = c;
        }
        int[] w = parseInts(WINDOW);
        for (int i = 0; i < 512; i++) win[i] = (float) (w[i] / 1e6);
        for (int k = 0; k < 32; k++)
            for (int i = 0; i < 64; i++) mat[k * 64 + i] = (float) Math.Cos((2 * k + 1) * (i - 16) * Math.PI / 64);
        for (int i = 0; i < 18; i++)
            for (int m = 0; m < 36; m++)
                mdctTab[i * 36 + m] = (float) (MDCT_SCALE * Math.Sin(Math.PI / 36 * (m + 0.5))
                        * Math.Cos(Math.PI / 72 * (2 * m + 1 + 18) * (2 * i + 1)));
        double[] c8 = {-0.6, -0.535, -0.33, -0.185, -0.095, -0.041, -0.0142, -0.0037};
        for (int i = 0; i < 8; i++) {
            double s = Math.Sqrt(1 + c8[i] * c8[i]);
            cs[i] = (float) (1 / s);
            ca[i] = (float) (c8[i] / s);
        }
    }

    private static int[] parseInts(string s) {
        int[] tmp = new int[s.Length / 2 + 2];
        int n = 0, v = 0, sign = 1;
        bool digits = false;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (c == '-') sign = -1;
            else if (c >= '0' && c <= '9') { v = v * 10 + (c - '0'); digits = true; }
            else {
                if (digits) tmp[n++] = sign * v;
                v = 0;
                sign = 1;
                digits = false;
            }
        }
        if (digits) tmp[n++] = sign * v;
        int[] r = new int[n];
        Array.Copy(tmp, 0, r, 0, n);
        return r;
    }

    private byte[] run(float[] pcm, int length, IProgress p) {
        int frames = (length + 1152 + 1151) / 1152 + 1;
        int slot = 144 * kbps * 1000;
        int baseBytes = slot / sr, remStep = slot % sr;
        buf = new byte[frames * (baseBytes + 1) + 64];
        len = 0;
        acc = 0;
        nbits = 0;
        float[] prev = new float[32 * 18], cur = new float[32 * 18];
        float[] xr = new float[GRANULE], xr34 = new float[GRANULE];
        int[] ix = new int[2 * GRANULE];
        float[] sign = new float[2 * GRANULE];
        int[] info = new int[2 * G_FIELDS];
        int pos = 0;
        long rem = 0;
        for (int f = 0; f < frames; f++) {
            if ((f & 63) == 0) {
                if (p.isCancelled()) throw new CancelledException();
                p.update((float) f / frames);
            }
            rem += remStep;
            int pad = 0;
            if (rem >= sr) { pad = 1; rem -= sr; }
            int frameBytes = baseBytes + pad;
            int mainBits = frameBytes * 8 - 32 - 136;
            int used = 0;
            for (int gr = 0; gr < 2; gr++) {
                // 18 bloques de 32 muestras por el banco de filtros
                for (int t = 0; t < 18; t++) {
                    analyze(pcm, length, pos, cur, t);
                    pos += 32;
                }
                // la MDCT necesita el gránulo anterior; las bandas impares van invertidas en frecuencia
                for (int b = 1; b < 32; b += 2)
                    for (int t = 1; t < 18; t += 2) cur[b * 18 + t] = -cur[b * 18 + t];
                for (int b = 0; b < 32; b++) mdct(prev, cur, b, xr);
                for (int b = 1; b < 32; b++)
                    for (int i = 0; i < 8; i++) {
                        int up = 18 * b - 1 - i, dn = 18 * b + i;
                        float u = xr[up], d = xr[dn];
                        xr[up] = u * cs[i] + d * ca[i];
                        xr[dn] = d * cs[i] - u * ca[i];
                    }
                float[] tmp = prev;
                prev = cur;
                cur = tmp;
                int budget = gr == 0 ? mainBits / 2 : mainBits - used;
                if (budget > 4095) budget = 4095;
                for (int i = 0; i < GRANULE; i++) sign[gr * GRANULE + i] = xr[i];
                quantize(xr, xr34, ix, gr * GRANULE, budget, info, gr * G_FIELDS);
                used += info[gr * G_FIELDS + G_PART23];
            }
            writeFrame(frameBytes, pad, ix, sign, info);
        }
        p.update(1f);
        byte[] @out = new byte[len];
        Array.Copy(buf, 0, @out, 0, len);
        return @out;
    }

    /** Un bloque de 32 muestras: ventana de 512, sumas parciales y matriz de cosenos (ISO 11172-3, anexo C). */
    private void analyze(float[] pcm, int length, int pos, float[] @out, int t) {
        fifoOff = (fifoOff + 512 - 32) & 511;
        for (int j = 0; j < 32; j++) {
            int s = pos + j;
            fifo[(fifoOff + 31 - j) & 511] = s < length ? pcm[s] : 0f;
        }
        for (int i = 0; i < 64; i++) {
            float acc2 = 0;
            for (int j = 0; j < 8; j++) {
                int k = i + 64 * j;
                acc2 += win[k] * fifo[(fifoOff + k) & 511];
            }
            y64[i] = acc2;
        }
        for (int k = 0; k < 32; k++) {
            float s = 0;
            int mb = k * 64;
            for (int i = 0; i < 64; i++) s += mat[mb + i] * y64[i];
            @out[k * 18 + t] = s;
        }
    }

    private void mdct(float[] prev, float[] cur, int band, float[] xr) {
        int b = band * 18;
        for (int i = 0; i < 18; i++) {
            float s = 0;
            int tb = i * 36;
            for (int m = 0; m < 18; m++) s += mdctTab[tb + m] * prev[b + m];
            for (int m = 0; m < 18; m++) s += mdctTab[tb + 18 + m] * cur[b + m];
            xr[b + i] = s;
        }
    }

    // ------------------------------------------------------------------ cuantización

    private void quantize(float[] xr, float[] xr34, int[] ix, int off, int budget, int[] info, int io) {
        float mx = 0;
        for (int i = 0; i < GRANULE; i++) {
            float a = (float) Math.Pow(Math.Abs(xr[i]), 0.75);
            xr34[i] = a;
            if (a > mx) mx = a;
        }
        if (mx == 0f) {
            for (int i = 0; i < GRANULE; i++) ix[off + i] = 0;
            countBits(ix, off, info, io);
            info[io + G_GAIN] = 210;
            return;
        }
        int lo = 0, hi = 255;
        while (lo < hi) {
            int mid = (lo + hi) / 2;
            if (quantizeWith(mid, xr34, mx, ix, off) && countBits(ix, off, info, io) <= budget) hi = mid;
            else lo = mid + 1;
        }
        if (!quantizeWith(hi, xr34, mx, ix, off) || countBits(ix, off, info, io) > budget) {
            for (int i = 0; i < GRANULE; i++) ix[off + i] = 0;      // no debería ocurrir
            countBits(ix, off, info, io);
        }
        info[io + G_GAIN] = hi;
    }

    private static bool quantizeWith(int gain, float[] xr34, float mx, int[] ix, int off) {
        double step = Math.Pow(2.0, -0.1875 * (gain - 210));
        if (mx * step + 0.4054 >= MAX_IX + 1) return false;
        float st = (float) step;
        for (int i = 0; i < GRANULE; i++) ix[off + i] = (int) (xr34[i] * st + 0.4054f);
        return true;
    }

    /** Cuenta los bits Huffman del gránulo y deja en info las regiones y tablas elegidas. */
    private int countBits(int[] ix, int off, int[] info, int io) {
        int i = GRANULE;
        while (i > 1 && ix[off + i - 1] == 0 && ix[off + i - 2] == 0) i -= 2;
        int c1end = i;
        while (i > 3 && ix[off + i - 1] <= 1 && ix[off + i - 2] <= 1 && ix[off + i - 3] <= 1 && ix[off + i - 4] <= 1) i -= 4;
        int bigEnd = i;
        int bitsA = 0, bitsB = 0, signs = 0;
        for (int k = bigEnd; k < c1end; k += 4) {
            int v = ix[off + k], w = ix[off + k + 1], x = ix[off + k + 2], y = ix[off + k + 3];
            bitsA += hlen[32][v * 8 + w * 4 + x * 2 + y];
            bitsB += 4;
            signs += v + w + x + y;
        }
        int bits = (bitsB < bitsA ? bitsB : bitsA) + signs;
        info[io + G_C1SEL] = bitsB < bitsA ? 1 : 0;
        info[io + G_C1END] = c1end;
        info[io + G_BIG] = bigEnd / 2;
        int r0 = 0, r1 = 0, a1 = 0, a2 = 0;
        if (bigEnd > 0) {
            int n = 0;
            while (sfb[n] < bigEnd) n++;
            r0 = SUBDV_R0[n];
            while (r0 > 0 && sfb[r0 + 1] > bigEnd) r0--;
            a1 = sfb[r0 + 1];
            r1 = SUBDV_R1[n];
            while (r1 > 0 && sfb[r0 + r1 + 2] > bigEnd) r1--;
            a2 = sfb[r0 + r1 + 2];
            if (a1 > bigEnd) a1 = bigEnd;
            if (a2 > bigEnd) a2 = bigEnd;
            if (a2 < a1) a2 = a1;
        }
        info[io + G_R0] = r0;
        info[io + G_R1] = r1;
        info[io + G_A1] = a1;
        info[io + G_A2] = a2;
        bits += chooseTable(ix, off, 0, a1, info, io + G_T0);
        bits += chooseTable(ix, off, a1, a2, info, io + G_T1);
        bits += chooseTable(ix, off, a2, bigEnd, info, io + G_T2);
        info[io + G_PART23] = bits;
        return bits;
    }

    private int chooseTable(int[] ix, int off, int start, int end, int[] info, int slot) {
        int mx = 0;
        for (int k = start; k < end; k++) if (ix[off + k] > mx) mx = ix[off + k];
        if (mx == 0) { info[slot] = 0; return 0; }
        int best = -1, bestBits = int.MaxValue;
        if (mx <= 15) {
            int t0, t1;
            if (mx == 1) { t0 = 1; t1 = 1; }
            else if (mx == 2) { t0 = 2; t1 = 3; }
            else if (mx == 3) { t0 = 5; t1 = 6; }
            else if (mx <= 5) { t0 = 7; t1 = 9; }
            else if (mx <= 7) { t0 = 10; t1 = 12; }
            else { t0 = 13; t1 = 15; }
            for (int t = t0; t <= t1; t++) {
                if (hlen[t] == null || XLEN[t] <= mx) continue;
                int b = pairBits(ix, off, start, end, t);
                if (b < bestBits) { bestBits = b; best = t; }
            }
        } else {
            int need = mx - 15;
            for (int g = 16; g <= 24; g += 8) {
                int t = g;
                while (t < g + 8 && (1 << LINBITS[t]) - 1 < need) t++;
                if (t == g + 8) continue;
                int b = pairBits(ix, off, start, end, t);
                if (b < bestBits) { bestBits = b; best = t; }
            }
        }
        info[slot] = best;
        return bestBits;
    }

    private int pairBits(int[] ix, int off, int start, int end, int t) {
        int bits = 0;
        if (t >= 16) {
            int[] l = hlen[t < 24 ? 16 : 24];
            int lb = LINBITS[t];
            for (int k = start; k < end; k += 2) {
                int x = ix[off + k], y = ix[off + k + 1];
                int xx = x > 15 ? 15 : x, yy = y > 15 ? 15 : y;
                bits += l[xx * 16 + yy];
                if (xx == 15) bits += lb;
                if (yy == 15) bits += lb;
                if (x != 0) bits++;
                if (y != 0) bits++;
            }
        } else {
            int[] l = hlen[t];
            int yl = XLEN[t];
            for (int k = start; k < end; k += 2) {
                int x = ix[off + k], y = ix[off + k + 1];
                bits += l[x * yl + y];
                if (x != 0) bits++;
                if (y != 0) bits++;
            }
        }
        return bits;
    }

    // ------------------------------------------------------------------ escritura

    private void writeFrame(int frameBytes, int pad, int[] ix, float[] sign, int[] info) {
        int start = len;
        // cabecera
        putBits(0xFFF, 12);
        putBits(1, 1);              // MPEG-1
        putBits(1, 2);              // Layer III
        putBits(1, 1);              // sin CRC
        putBits(brIndex, 4);
        putBits(srIndex, 2);
        putBits(pad, 1);
        putBits(0, 1);              // privado
        putBits(3, 2);              // mono
        putBits(0, 2);
        putBits(0, 1);              // copyright
        putBits(1, 1);              // original
        putBits(0, 2);              // sin énfasis
        // información lateral (mono: 17 bytes)
        putBits(0, 9);              // main_data_begin: sin reserva de bits
        putBits(0, 5);
        putBits(0, 4);              // scfsi
        for (int gr = 0; gr < 2; gr++) {
            int io = gr * G_FIELDS;
            putBits(info[io + G_PART23], 12);
            putBits(info[io + G_BIG], 9);
            putBits(info[io + G_GAIN], 8);
            putBits(0, 4);          // scalefac_compress: sin factores de escala
            putBits(0, 1);          // bloques largos
            putBits(info[io + G_T0], 5);
            putBits(info[io + G_T1], 5);
            putBits(info[io + G_T2], 5);
            putBits(info[io + G_R0], 4);
            putBits(info[io + G_R1], 3);
            putBits(0, 1);          // preflag
            putBits(0, 1);          // scalefac_scale
            putBits(info[io + G_C1SEL], 1);
        }
        // datos principales
        for (int gr = 0; gr < 2; gr++) {
            int io = gr * G_FIELDS, off = gr * GRANULE;
            int big = 2 * info[io + G_BIG];
            int a1 = info[io + G_A1], a2 = info[io + G_A2];
            writePairs(ix, sign, off, 0, a1, info[io + G_T0]);
            writePairs(ix, sign, off, a1, a2, info[io + G_T1]);
            writePairs(ix, sign, off, a2, big, info[io + G_T2]);
            int c1end = info[io + G_C1END];
            int tab = info[io + G_C1SEL] == 1 ? 33 : 32;
            for (int k = big; k < c1end; k += 4) {
                int v = ix[off + k], w = ix[off + k + 1], x = ix[off + k + 2], y = ix[off + k + 3];
                int idx = v * 8 + w * 4 + x * 2 + y;
                putBits(hcode[tab][idx], hlen[tab][idx]);
                if (v != 0) putBits(sign[off + k] < 0 ? 1 : 0, 1);
                if (w != 0) putBits(sign[off + k + 1] < 0 ? 1 : 0, 1);
                if (x != 0) putBits(sign[off + k + 2] < 0 ? 1 : 0, 1);
                if (y != 0) putBits(sign[off + k + 3] < 0 ? 1 : 0, 1);
            }
        }
        // relleno hasta el tamaño exacto de la trama
        int written = (len - start) * 8 + nbits;
        int total = frameBytes * 8;
        if (written > total) throw new InvalidOperationException("Trama MP3 desbordada");
        while (written < total) {
            int n = total - written;
            if (n > 24) n = 24;
            putBits(0, n);
            written += n;
        }
    }

    private void writePairs(int[] ix, float[] sign, int off, int start, int end, int t) {
        if (t == 0 || end <= start) return;
        int @base = t >= 24 ? 24 : (t >= 16 ? 16 : t);
        int[] code = hcode[@base], l = hlen[@base];
        int lb = LINBITS[t];
        int yl = t >= 16 ? 16 : XLEN[t];
        for (int k = start; k < end; k += 2) {
            int x = ix[off + k], y = ix[off + k + 1];
            int xx = x, yy = y;
            if (t >= 16) {
                if (xx > 15) xx = 15;
                if (yy > 15) yy = 15;
            }
            int idx = xx * yl + yy;
            putBits(code[idx], l[idx]);
            if (t >= 16 && xx == 15) putBits(x - 15, lb);
            if (x != 0) putBits(sign[off + k] < 0 ? 1 : 0, 1);
            if (t >= 16 && yy == 15) putBits(y - 15, lb);
            if (y != 0) putBits(sign[off + k + 1] < 0 ? 1 : 0, 1);
        }
    }

    private void putBits(int value, int n) {
        while (n > 0) {
            int free = 8 - nbits;
            int take = n < free ? n : free;
            int shift = n - take;
            int bits = (value >> shift) & ((1 << take) - 1);
            acc = (acc << take) | bits;
            nbits += take;
            n -= take;
            if (nbits == 8) {
                buf[len++] = (byte) acc;
                acc = 0;
                nbits = 0;
            }
        }
    }
}
}
