// Triggers a browser "Save as…" download from a Blazor DotNetStreamReference.
// Used to download PDFs fetched via authenticated HttpClient (Bearer JWT
// can't be attached to plain <a href> navigations in WASM).

export async function downloadFromStream(fileName, contentStreamReference, mimeType) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: mimeType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? '';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
}
