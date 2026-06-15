import re
with open(r"E:\temp_sdk_fix.cs", "r", encoding="utf-8") as f:
    content = f.read()
content = content.replace(chr(0xfffd), "")
content = content.replace("dhEventInfo(\"", "dhEventInfo(\"Camera opened.\")
				if (false && dhEventInfo")
with open(r"E:\temp_sdk_fix_py.cs", "w", encoding="utf-8") as f:
    f.write(content)
print("done")
