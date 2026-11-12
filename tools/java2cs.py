"""Convierte el motor Java (app/src/.../engine) a C# para la versión de Windows.

Uso: python3 java2cs.py ENGINE_JAVA_DIR OUT_DIR
Solo cubre el subconjunto de Java que usa el motor; Progress y Note se escriben a mano (windows/engine/Common.cs).
"""
import os
import re
import sys

FILES = ["BasicPitch", "WeightFile", "MidiWriter", "Mp3Encoder", "Synth", "DrumKit", "Resampler"]

KEYWORDS = ["out", "in", "base", "lock", "ref", "params", "object", "event", "checked", "fixed",
            "operator", "is", "as", "internal", "readonly", "sealed", "unchecked", "virtual",
            "decimal", "ulong", "uint", "ushort", "sbyte"]

MATH = {"sqrt": "Sqrt", "exp": "Exp", "log": "Log", "log10": "Log10", "pow": "Pow", "sin": "Sin",
        "cos": "Cos", "abs": "Abs", "max": "Max", "min": "Min", "floor": "Floor", "ceil": "Ceiling",
        "tanh": "Tanh", "atan2": "Atan2", "rint": "Round", "PI": "PI"}


def convert(src):
    s = src
    # palabras reservadas de C# usadas como identificadores en Java
    for k in KEYWORDS:
        s = re.sub(r"\b%s\b" % k, "@" + k, s)
    s = re.sub(r"^package [\w.]+;\s*", "", s, flags=re.M)
    s = re.sub(r"^import [\w.]+;\s*\n", "", s, flags=re.M)
    s = s.replace("@Override", "")
    s = re.sub(r"\s+throws [\w., ]+(?=\s*\{)", "", s)
    # constantes
    s = re.sub(r"\b(public|private) static final (int|double|float|long) (?=[^;{]*;)(?![^;]*\bnew\b)(?![^;]*Math\.)",
               r"\1 const \2 ", s)
    s = re.sub(r"\bstatic final\b", "static readonly", s)
    s = re.sub(r"\b(private|public|protected) final\b", r"\1 readonly", s)
    s = re.sub(r"\bpublic readonly class\b", "public sealed class", s)
    s = re.sub(r"\bpublic static readonly class\b", "public sealed class", s)
    s = re.sub(r"\bstatic readonly class\b", "sealed class", s)
    s = re.sub(r"\bfinal ", "", s)
    s = re.sub(r"\bboolean\b", "bool", s)
    s = re.sub(r"\bString\b", "string", s)
    s = s.replace("Double.NEGATIVE_INFINITY", "double.NegativeInfinity")
    s = s.replace("Integer.MAX_VALUE", "int.MaxValue")
    s = s.replace("Float.POSITIVE_INFINITY", "float.PositiveInfinity")
    s = s.replace("Float.NEGATIVE_INFINITY", "float.NegativeInfinity")
    s = s.replace("Float.intBitsToFloat(", "JU.IntBitsToFloat(")
    s = s.replace("IllegalStateException", "InvalidOperationException")
    s = re.sub(r"\bHashMap<", "Dictionary<", s)
    s = s.replace(".containsKey(", ".ContainsKey(")
    s = re.sub(r"(\w+)\.equals\((\w+)\)", r"(\1 == \2)", s)
    s = s.replace(".length()", ".Length")
    s = re.sub(r"\.charAt\(([^()]*)\)", r"[\1]", s)
    s = s.replace("IllegalArgumentException", "ArgumentException")
    s = s.replace("System.arraycopy(", "Array.Copy(")
    s = s.replace("Arrays.fill(", "JU.Fill(")
    s = s.replace("Arrays.copyOfRange(", "JU.CopyOfRange(")
    s = s.replace("Arrays.copyOf(", "JU.CopyOf(")
    s = s.replace("Arrays.sort(", "Array.Sort(")
    s = re.sub(r"java\.util\.Collections\.sort\((\w+)\)", r"\1.Sort()", s)
    s = re.sub(r"\bCollections\.sort\((\w+)\)", r"\1.Sort()", s)
    s = s.replace("new java.util.ArrayList<", "new List<").replace("new ArrayList<", "new List<")
    s = s.replace("List<Integer>", "List<int>")
    s = re.sub(r"\bMath\.round\(", "JU.Round(", s)
    s = re.sub(r"\bMath\.signum\(", "JU.Signum(", s)
    s = re.sub(r"\bMath\.(\w+)", lambda m: "Math." + MATH.get(m.group(1), m.group(1)), s)
    s = re.sub(r"\.length\b", ".Length", s)
    s = s.replace(".size()", ".Count")
    s = re.sub(r"!(\w+)\.isEmpty\(\)", r"\1.Count > 0", s)
    s = s.replace(".isEmpty()", ".Count == 0")
    s = s.replace(".nextDouble()", ".NextDouble()")
    # constructores de visibilidad de paquete -> internal
    s = re.sub(r"^(\s+)(Result\()", r"\1internal \2", s, flags=re.M)
    s = re.sub(r"\.add\(", ".Add(", s)
    s = re.sub(r"\.remove\((\w+)\)", r".RemoveAt(\1)", s)
    s = re.sub(r"\.clear\(\)", ".Clear()", s)
    # .get(x) -> [x] (un nivel de paréntesis anidados)
    s = re.sub(r"\.get\(((?:[^()]|\([^()]*\))*)\)", r"[\1]", s)
    # for-each
    s = re.sub(r"for \(([\w<>\[\]]+) (\w+) : ", r"foreach (\1 \2 in ", s)
    # progreso
    s = s.replace("Progress.Cancelled", "CancelledException")
    s = s.replace("Progress.NONE", "ProgressUtil.NONE")
    s = re.sub(r"\bProgress\b", "IProgress", s)
    # cuerpo dentro del namespace
    head = "// Generado desde el motor Java con tools/java2cs.py. No editar a mano.\n" \
           "using System;\nusing System.Collections.Generic;\nusing System.IO;\n\nnamespace MelodiaMidi.Engine\n{\n"
    return head + s.rstrip() + "\n}\n"


def main(src_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name in FILES:
        src = open(os.path.join(src_dir, name + ".java"), encoding="utf-8").read()
        cs = convert(src)
        if name == "Synth":
            # en C# un método no puede llamarse igual que una variable local que lo usa
            cs = cs.replace("private float[] table(int key", "private float[] makeTable(int key")
            cs = re.sub(r"= table\(", "= makeTable(", cs)
            cs = re.sub(r"Map<Integer, float\[\]> tableCache = new (?:HashMap|Dictionary)<Integer, float\[\]>\(\)",
                        "Dictionary<int, float[]> tableCache = new Dictionary<int, float[]>()", cs)
            cs = cs.replace("Integer k = key;\n        float[] cached = tableCache[k];\n        if (cached != null) return cached;",
                            "int k = key;\n        float[] cached;\n        if (tableCache.TryGetValue(k, out cached)) return cached;")
            cs = cs.replace("tableCache.put(k, t);", "tableCache[k] = t;")
        if name == "WeightFile":
            cs = re.sub(r"map\.put\((\w+), (\w+)\);", r"map[\1] = \2;", cs)
            cs = cs.replace("map.RemoveAt(name)", "map.Remove(name)")
        open(os.path.join(out_dir, name + ".cs"), "w", encoding="utf-8").write(cs)
        print("ok", name)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
