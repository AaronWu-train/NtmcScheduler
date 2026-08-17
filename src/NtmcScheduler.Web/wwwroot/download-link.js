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

window.scrollToScheduleCell = id => {
    const cell = document.getElementById(id);
    if (!cell) return;
    cell.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
    cell.classList.remove("located-cell");
    requestAnimationFrame(() => cell.classList.add("located-cell"));
    setTimeout(() => cell.classList.remove("located-cell"), 1800);
};
