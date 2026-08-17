document.addEventListener("click", event => {
    const link = event.target.closest('a[href^="/download/"]');
    if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const download = document.createElement("a");
    download.href = link.href;
    download.download = "";
    document.body.append(download);
    download.click();
    download.remove();
});
