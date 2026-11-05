"""Generate and validate the Fab ASCII protocol Word document using the standard library."""

from pathlib import Path
import re
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
XML = "http://www.w3.org/XML/1998/namespace"
ET.register_namespace("w", W)
ET.register_namespace("r", R)
ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs" / "Fab通信协议_ASCII版.md"
OUTPUT = SOURCE.with_suffix(".docx")


def tag(name):
    """Return a WordprocessingML qualified element name."""
    return f"{{{W}}}{name}"


def add(parent, name, **attributes):
    """Append a WordprocessingML element with qualified attributes."""
    return ET.SubElement(parent, tag(name), {tag(k): str(v) for k, v in attributes.items()})


def paragraph(parent, text="", style=None):
    """Add a paragraph, preserving the original text and optional paragraph style."""
    node = add(parent, "p")
    if style:
        properties = add(node, "pPr")
        add(properties, "pStyle", val=style)
    run = add(node, "r")
    content = add(run, "t")
    content.set(f"{{{XML}}}space", "preserve")
    content.text = text
    return node


def table(parent, lines):
    """Render Markdown table rows as a fixed-width Word table with repeated headings."""
    rows = []
    for line in lines:
        values = [value.strip() for value in line.strip().strip("|").split("|")]
        # Skip the Markdown separator rather than publishing it as a data row.
        if all(re.fullmatch(r":?-+:?", value) for value in values):
            continue
        rows.append(values)
    columns = len(rows[0])
    widths = {
        2: [2500, 7138],
        3: [1100, 3400, 5138],
        4: [1500, 1600, 1700, 4838],
        6: [700, 700, 2400, 650, 1400, 3788],
    }.get(columns, [9638 // columns] * columns)
    node = add(parent, "tbl")
    properties = add(node, "tblPr")
    add(properties, "tblW", w=9638, type="dxa")
    add(properties, "tblLayout", type="fixed")
    borders = add(properties, "tblBorders")
    for side in ("top", "left", "bottom", "right", "insideH", "insideV"):
        add(borders, side, val="single", sz=4, color="CBD5E1")
    margins = add(properties, "tblCellMar")
    for side in ("top", "left", "bottom", "right"):
        add(margins, side, w=75, type="dxa")
    grid = add(node, "tblGrid")
    for width in widths:
        add(grid, "gridCol", w=width)
    for index, values in enumerate(rows):
        row = add(node, "tr")
        row_properties = add(row, "trPr")
        add(row_properties, "cantSplit")
        # Repeat table headings on each page for long variable/event dictionaries.
        if index == 0:
            add(row_properties, "tblHeader")
        for column, value in enumerate(values):
            cell = add(row, "tc")
            cell_properties = add(cell, "tcPr")
            add(cell_properties, "tcW", w=widths[column], type="dxa")
            if index == 0:
                add(cell_properties, "shd", fill="E2E8F0")
            paragraph(cell, value.replace("`", ""), "TableText")
    paragraph(parent)


def document_xml(text):
    """Convert the protocol source into Word paragraphs, headings, tables, and SML blocks."""
    document = ET.Element(tag("document"))
    body = add(document, "body")
    lines = text.splitlines()
    position = 0
    code = False
    while position < len(lines):
        line = lines[position]
        # Fenced blocks contain literal SML; do not treat their angle brackets as XML.
        if line.startswith("```"):
            code = not code
            position += 1
            continue
        if code:
            paragraph(body, line, "Code")
        elif line.startswith("|"):
            end = position
            while end < len(lines) and lines[end].startswith("|"):
                end += 1
            table(body, lines[position:end])
            position = end
            continue
        elif line.startswith("# "):
            paragraph(body, line[2:], "Title")
        elif line.startswith("## "):
            paragraph(body, line[3:], "Heading1")
        elif line.startswith("### "):
            paragraph(body, line[4:], "Heading2")
        elif line.strip():
            paragraph(body, line.replace("`", ""))
        position += 1
    if code:
        raise ValueError("Unclosed SML fence in protocol source")
    section = add(body, "sectPr")
    add(section, "headerReference", type="default").set(f"{{{R}}}id", "rIdHeader")
    add(section, "footerReference", type="default").set(f"{{{R}}}id", "rIdFooter")
    add(section, "pgSz", w=11906, h=16838)
    add(section, "pgMar", top=1134, right=1134, bottom=1134, left=1134, header=567, footer=567, gutter=0)
    return ET.tostring(document, encoding="utf-8", xml_declaration=True)


def style_xml():
    """Build readable Chinese/Latin styles with navigable heading levels."""
    styles = ET.Element(tag("styles"))
    defaults = add(styles, "docDefaults")
    run_defaults = add(add(defaults, "rPrDefault"), "rPr")
    add(run_defaults, "rFonts", ascii="Calibri", hAnsi="Calibri", eastAsia="Microsoft YaHei")
    add(run_defaults, "sz", val=21)
    add(run_defaults, "lang", val="en-US", eastAsia="zh-CN")
    paragraph_defaults = add(add(defaults, "pPrDefault"), "pPr")
    add(paragraph_defaults, "spacing", after=100, line=290, lineRule="auto")
    for identifier, size, color, outline in (
        ("Normal", 21, "1E293B", None),
        ("Title", 36, "17365D", None),
        ("Heading1", 29, "17365D", 0),
        ("Heading2", 24, "1F4E79", 1),
        ("Code", 18, "111827", None),
        ("TableText", 18, "1E293B", None),
    ):
        node = add(styles, "style", type="paragraph", styleId=identifier)
        add(node, "name", val=identifier)
        if identifier == "Normal":
            node.set(tag("default"), "1")
        else:
            add(node, "basedOn", val="Normal")
        properties = add(node, "pPr")
        if outline is not None:
            add(properties, "outlineLvl", val=outline)
            add(properties, "keepNext")
            add(properties, "spacing", before=240, after=120)
        if identifier == "Title":
            add(properties, "spacing", before=200, after=320)
        if identifier == "Code":
            add(properties, "spacing", after=0, line=230, lineRule="auto")
            add(properties, "shd", fill="F1F5F9")
        if identifier == "TableText":
            add(properties, "spacing", after=30, line=240, lineRule="auto")
        run = add(node, "rPr")
        add(run, "sz", val=size)
        add(run, "color", val=color)
        if identifier in ("Title", "Heading1", "Heading2"):
            add(run, "b")
        if identifier == "Code":
            add(run, "rFonts", ascii="Consolas", hAnsi="Consolas", eastAsia="Microsoft YaHei")
    return ET.tostring(styles, encoding="utf-8", xml_declaration=True)


def furniture_xml(footer=False):
    """Build the protocol header or a footer containing a live page-number field."""
    root = ET.Element(tag("ftr" if footer else "hdr"))
    node = paragraph(root, "GWM-SECS-FAB-ASCII-001 | V1.0 | 对接评审稿" if not footer else "Equipment–Fab 通信协议  |  第 ")
    if footer:
        field = add(node, "fldSimple", instr="PAGE")
        add(add(field, "r"), "t").text = "1"
        add(add(node, "r"), "t").text = " 页"
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def validate(path, source):
    """Check ZIP integrity, every XML part, key protocol content, and ASCII dictionary rows."""
    with ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError("Corrupt Word archive")
        for name in archive.namelist():
            if name.endswith((".xml", ".rels")):
                ET.fromstring(archive.read(name))
        document = ET.fromstring(archive.read("word/document.xml"))
        content = "".join(node.text or "" for node in document.iter(tag("t")))
        for expected in ("S6F11", "S6F12", "U2 VID", 'A "5"', "WaferResult", "SurfaceS3Processed"):
            if expected not in content:
                raise ValueError(f"Missing protocol content: {expected}")
        for excluded in ("S3F17", "S14F9", "S16F15", "S2F49", "SpoolCountActual", "{{VID_TABLE}}"):
            if excluded in content:
                raise ValueError(f"Out-of-scope content: {excluded}")
        dictionary = source.split("## 附录 A：", 1)[1].split("## 附录 B：", 1)[0]
        variables = [line for line in dictionary.splitlines() if re.match(r"\| \d+ \|", line)]
        if len(variables) != 46 or any(line.split("|")[4].strip() != "A" for line in variables):
            raise ValueError("Expected 46 in-scope variables, all ASCII")
        events = source.split("## 附录 B：", 1)[1].split("## 附录 C：", 1)[0]
        if len(re.findall(r"^\| \d+ \|", events, re.MULTILINE)) != 38:
            raise ValueError("Expected 38 event definitions")
        print(f"Validated: {len(variables)} ASCII variables, 38 events, {len(list(document.iter(tag('tbl'))))} tables")


def main():
    """Package the reviewed Markdown source into a standalone editable DOCX and verify it."""
    text = SOURCE.read_text(encoding="utf-8-sig")
    content_types = '''<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
</Types>'''
    package_rels = '''<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>'''
    document_rels = '''<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
<Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
<Relationship Id="rIdFooter" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
</Relationships>'''
    with ZipFile(OUTPUT, "w", ZIP_DEFLATED) as archive:
        archive.writestr("[Content_Types].xml", content_types)
        archive.writestr("_rels/.rels", package_rels)
        archive.writestr("word/_rels/document.xml.rels", document_rels)
        archive.writestr("word/document.xml", document_xml(text))
        archive.writestr("word/styles.xml", style_xml())
        archive.writestr("word/header1.xml", furniture_xml())
        archive.writestr("word/footer1.xml", furniture_xml(footer=True))
    validate(OUTPUT, text)
    print(f"Created: {OUTPUT} ({OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
