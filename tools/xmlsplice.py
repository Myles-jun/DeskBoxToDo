import io, re, sys

src, bak, dest = sys.argv[1], sys.argv[2], sys.argv[3]
start, end = sys.argv[4], sys.argv[5]
anchor = sys.argv[6]

backup = io.open(bak, encoding="utf-8").read()
match = re.search(re.escape(start) + r".*?" + re.escape(end), backup, re.S)
if not match:
    print("block not found in backup")
    sys.exit(1)

current = io.open(src, encoding="utf-8").read()
if start in current:
    print("block already present")
    sys.exit(0)

current = current.replace(anchor, match.group(0) + "\n" + anchor, 1)
io.open(dest, "w", encoding="utf-8").write(current)
print("inserted", len(match.group(0)), "chars")
