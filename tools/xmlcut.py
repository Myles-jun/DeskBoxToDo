import io, re, sys

path, start, end, stub = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
text = io.open(path, encoding="utf-8").read()
pattern = re.escape(start) + r".*?" + re.escape(end)
new_text, count = re.subn(pattern, stub, text, flags=re.S)
io.open(path, "w", encoding="utf-8").write(new_text)
print("cut", count, "block(s), saved", len(text) - len(new_text), "chars")
