import sys

with open('VisionMeasure/From/MainFrm.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Fix 爆管 case (lines 3004-3010 in original, now shifted due to earlier edit)
# Find the case statements by content
for i, line in enumerate(lines):
    # Fix 爆管 case
    if line.strip() == 'case "爆管":' and 'Cv2.DrawContours' in lines[i+1]:
        # Check this is the old pattern (not already fixed)
        if 'if (_Config.Camera5IFBaoGuan)' not in lines[i+1]:
            indent9 = '\t' * 9
            indent10 = '\t' * 10
            indent11 = '\t' * 11
            lines[i]   = f'{indent9}case "爆管":\n'
            lines[i+1] = f'{indent10}if (_Config.Camera5IFBaoGuan)\n'
            lines[i+2] = f'{indent10}{{\n'
            lines[i+3] = f'{indent11}Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);\n'
            lines[i+4] = f'{indent11}if (classBuilder.Length > 0) classBuilder.Append("; ");\n'
            lines[i+5] = f'{indent11}classBuilder.Append("爆管");\n'
            lines[i+6] = f'{indent11}result_Class_str += "1";\n'
            lines[i+7] = f'{indent10}}}\n'
            lines[i+8] = f'{indent10}else\n'
            lines[i+9] = f'{indent10}{{\n'
            lines[i+10]= f'{indent11}result_Class_str += "0";\n'
            lines[i+11]= f'{indent10}}}\n'
            lines[i+12]= f'{indent10}break;\n'
            print(f'Fixed 爆管 at line {i+1}')
            break

# Fix 未剪断 case
for i, line in enumerate(lines):
    if line.strip() == 'case "未剪断":' and 'Cv2.DrawContours' in lines[i+1]:
        if 'if (_Config.Camera5IFWeiJianDuan)' not in lines[i+1]:
            indent9 = '\t' * 9
            indent10 = '\t' * 10
            indent11 = '\t' * 11
            lines[i]   = f'{indent9}case "未剪断":\n'
            lines[i+1] = f'{indent10}if (_Config.Camera5IFWeiJianDuan)\n'
            lines[i+2] = f'{indent10}{{\n'
            lines[i+3] = f'{indent11}Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);\n'
            lines[i+4] = f'{indent11}if (classBuilder.Length > 0) classBuilder.Append("; ");\n'
            lines[i+5] = f'{indent11}classBuilder.Append("未剪断");\n'
            lines[i+6] = f'{indent11}result_Class_str += "1";\n'
            lines[i+7] = f'{indent10}}}\n'
            lines[i+8] = f'{indent10}else\n'
            lines[i+9] = f'{indent10}{{\n'
            lines[i+10]= f'{indent11}result_Class_str += "0";\n'
            lines[i+11]= f'{indent10}}}\n'
            lines[i+12]= f'{indent10}break;\n'
            print(f'Fixed 未剪断 at line {i+1}')
            break

# Fix 斜口 case
for i, line in enumerate(lines):
    if line.strip() == 'case "斜口":' and 'Cv2.DrawContours' in lines[i+1]:
        if 'if (_Config.Camera5IFXieKou)' not in lines[i+1]:
            indent9 = '\t' * 9
            indent10 = '\t' * 10
            indent11 = '\t' * 11
            lines[i]   = f'{indent9}case "斜口":\n'
            lines[i+1] = f'{indent10}if (_Config.Camera5IFXieKou)\n'
            lines[i+2] = f'{indent10}{{\n'
            lines[i+3] = f'{indent11}Cv2.DrawContours(resultImage, contours, -1, new Scalar(0, 0, 255), 2);\n'
            lines[i+4] = f'{indent11}if (classBuilder.Length > 0) classBuilder.Append("; ");\n'
            lines[i+5] = f'{indent11}classBuilder.Append("斜口");\n'
            lines[i+6] = f'{indent11}result_Class_str += "1";\n'
            lines[i+7] = f'{indent10}}}\n'
            lines[i+8] = f'{indent10}else\n'
            lines[i+9] = f'{indent10}{{\n'
            lines[i+10]= f'{indent11}result_Class_str += "0";\n'
            lines[i+11]= f'{indent10}}}\n'
            lines[i+12]= f'{indent10}break;\n'
            print(f'Fixed 斜口 at line {i+1}')
            break

with open('VisionMeasure/From/MainFrm.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print('Done')
