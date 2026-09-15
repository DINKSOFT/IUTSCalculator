Add-Type -Path 'packages\BouncyCastle.Cryptography.2.6.1\lib\net461\BouncyCastle.Cryptography.dll'
Add-Type -Path 'packages\iTextSharp.5.5.13.4\lib\net461\itextsharp.dll'
$pdfPath = 'IUTSCalculator\Feuille de route Sigma Paie & RH.pdf'
$reader = New-Object iTextSharp.text.pdf.PdfReader($pdfPath)

for ($i = 1; $i -le $reader.NumberOfPages; $i++) {
    $text = [iTextSharp.text.pdf.parser.PdfTextExtractor]::GetTextFromPage($reader, $i)
    if ($text -match 'PAI-06' -or $text -match 'saisie') {
        Write-Host "================ PAGE $i ================"
        Write-Host $text
    }
}
$reader.Close()
