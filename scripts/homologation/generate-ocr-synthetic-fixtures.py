"""Generate labeled, synthetic OCR inputs. Requires Pillow and pypdf; no patient data."""
from pathlib import Path
import argparse
from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfWriter, PdfReader
from pypdf.generic import DictionaryObject, NameObject, DecodedStreamObject


def digital(path):
    writer = PdfWriter()
    page = writer.add_blank_page(width=595, height=842)
    page[NameObject('/Resources')] = DictionaryObject({NameObject('/Font'): DictionaryObject({
        NameObject('/F1'): DictionaryObject({NameObject('/Type'): NameObject('/Font'),
            NameObject('/Subtype'): NameObject('/Type1'), NameObject('/BaseFont'): NameObject('/Helvetica')})})})
    stream = DecodedStreamObject()
    stream.set_data(b'BT /F1 18 Tf 50 760 Td (Documento digital SINTETICO 001) Tj ET')
    page[NameObject('/Contents')] = writer._add_object(stream)
    writer.write(path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    font_paths = [Path('C:/Windows/Fonts/arial.ttf'), Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')]
    font = ImageFont.truetype(str(next(p for p in font_paths if p.exists())), 42)
    variants = {
        'scanned': ['Documento SINTETICO 002', 'Paciente ficticio: Pessoa Exemplo', 'Atendimento: TESTE-002'],
        'accents': ['Documento SINTETICO 003', 'João da Conceição', 'Avaliação, prescrição e evolução clínica'],
        'table': ['Documento SINTETICO 004', 'Procedimento             Quantidade', 'Consulta                       1', 'Exame                          2'],
        'missing-fields': ['Documento SINTETICO 005', 'Paciente: campo ausente', 'Atendimento: nao informado'],
        'contradictory': ['Documento SINTETICO 006', 'Atendimento: TESTE-A', 'Atendimento: TESTE-B'],
    }
    base = None
    for name, lines in variants.items():
        image = Image.new('RGB', (1654, 2339), 'white')
        draw = ImageDraw.Draw(image)
        for index, line in enumerate(lines):
            draw.text((120, 180 + index * 100), line, font=font, fill='black')
        image.save(args.output / (name + '.pdf'), resolution=200)
        if name == 'scanned':
            base = image
    base.rotate(6, resample=Image.Resampling.BICUBIC, fillcolor='white').save(args.output / 'tilted.pdf', resolution=200)
    base.resize((620, 877)).save(args.output / 'low-resolution.pdf', resolution=75)
    digital(args.output / 'searchable.pdf')
    writer = PdfWriter()
    for name in ['searchable', 'scanned']:
        for page in PdfReader(args.output / (name + '.pdf')).pages:
            writer.add_page(page)
    writer.write(args.output / 'mixed.pdf')
    print(f'{len(list(args.output.glob("*.pdf")))} synthetic PDF fixtures generated.')


if __name__ == '__main__':
    main()
