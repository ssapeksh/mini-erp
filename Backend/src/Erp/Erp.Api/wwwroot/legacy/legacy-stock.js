/* ==========================================================================
   legacy-stock.js - classic jQuery page script
   Interview topics: $(document).ready, selectors, AJAX ($.ajax / $.getJSON,
   .done/.fail/.always), event DELEGATION with .on(), debounce, .prop vs .attr,
   .text vs .html vs .val, .each, closures, the anti-forgery header, XSS-safe
   rendering with .text()
   ========================================================================== */

// $(function () { ... }) is the short form of $(document).ready(...):
// runs when the DOM is parsed, before images load (window.onload waits for everything).
$(function () {
    'use strict';

    const $tbody = $('#stockTable tbody');            // ID selector + descendant
    const $status = $('#status');
    // Anti-forgery token rendered by @Html.AntiForgeryToken() (attribute selector)
    const antiForgeryToken = $('input[name="__RequestVerificationToken"]').val();   // .val(): form value

    let allRows = [];                                 // closure: the handlers below keep access to it

    function load() {
        $status.text('Loading...');
        // $.getJSON = $.ajax shorthand for a GET returning JSON; promise-style callbacks
        $.getJSON('/legacy/stock-data', { warehouseId: $('#warehouse').val() || undefined })
            .done(function (rows) {
                allRows = rows;
                render();
            })
            .fail(function (xhr) {
                $status.text('Could not load stock (' + xhr.status + ').');
            });
    }

    function render() {
        const term = $('#search').val().trim().toLowerCase();
        // .prop('checked') reads the LIVE state; .attr('checked') only reads the HTML attribute as written
        const onlyReorder = $('#onlyReorder').prop('checked');

        const rows = allRows.filter(function (r) {
            return (!term || r.code.toLowerCase().includes(term) || r.name.toLowerCase().includes(term))
                && (!onlyReorder || r.stockStatus === 'Reorder');
        });

        $tbody.empty();
        $.each(rows, function (index, r) {            // $.each: index first, then the value
            const $tr = $('<tr>');
            // .text() (not .html()) so names are ESCAPED: no XSS even if a name contains <script>
            $tr.append($('<td>').text(r.code));
            $tr.append($('<td>').text(r.name));
            $tr.append($('<td>').text(r.warehouse));
            $tr.append($('<td class="num">').text(r.quantity.toFixed(2)));
            $tr.append($('<td class="num">').text(r.reorderLevel.toFixed(2)));
            $tr.append($('<td>').append($('<span class="pill">').addClass('pill-' + r.stockStatus.toLowerCase()).text(r.stockStatus)));
            $tr.append($('<td>').append(
                $('<button type="button" class="btn btn-small btn-reorder">Reorder</button>')
                    .attr('data-item-id', r.itemId)
                    .attr('data-warehouse-id', r.warehouseId)
                    .prop('disabled', r.stockStatus === 'OK')));
            $tbody.append($tr);
        });
        $status.text(rows.length + ' of ' + allRows.length + ' rows');
    }

    // DEBOUNCE: wait until the user stops typing for 300 ms before filtering.
    // (THROTTLE would instead run at most once every N ms, e.g. for scroll.)
    function debounce(fn, ms) {
        let timer;                                    // closure keeps the timer between calls
        return function () {
            const args = arguments, self = this;
            clearTimeout(timer);
            timer = setTimeout(function () { fn.apply(self, args); }, ms);   // apply: call with this + args array
        };
    }

    $('#search').on('input', debounce(render, 300));
    $('#onlyReorder').on('change', render);
    $('#warehouse').on('change', load);

    // EVENT DELEGATION: one handler on the table body for buttons that don't exist
    // yet (rows are rebuilt on every render). Clicks bubble up from the button.
    $tbody.on('click', '.btn-reorder', function () {
        const $btn = $(this);
        const qty = prompt('Quantity to reorder?', '50');
        if (qty === null) return;

        $btn.prop('disabled', true);
        $.ajax({
            url: '/legacy/reorder-request',
            type: 'POST',
            contentType: 'application/json',
            headers: { 'RequestVerificationToken': antiForgeryToken },   // CSRF protection for cookie auth
            data: JSON.stringify({
                itemId: Number($btn.data('itemId')),        // .data() reads data-item-id
                warehouseId: Number($btn.data('warehouseId')),
                qty: Number(qty),
                note: 'Requested from legacy stock report'
            })
        })
            .done(function (res) { showToast(res.message); })
            .fail(function (xhr) {
                const msg = (xhr.responseJSON && xhr.responseJSON.message) || ('Request failed (' + xhr.status + ')');
                showToast(msg, true);
            })
            .always(function () { $btn.prop('disabled', false); });
    });

    function showToast(message, isError) {
        $('#toast').text(message).toggleClass('toast-error', !!isError).prop('hidden', false)
            .stop(true).fadeIn(150).delay(2500).fadeOut(400, function () { $(this).prop('hidden', true); });
    }

    load();
});
