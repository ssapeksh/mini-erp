import { AfterViewInit, Directive, ElementRef, inject } from '@angular/core';

/**
 * ATTRIBUTE DIRECTIVE (changes behaviour/appearance of an existing element,
 * like ngClass): <input appAutofocus>. Focus must wait until the element is in
 * the DOM, hence ngAfterViewInit rather than the constructor or ngOnInit.
 */
@Directive({
  selector: '[appAutofocus]',
  host: { '[attr.data-autofocus]': 'true' },     // host binding: adds an attribute to the element
})
export class AutofocusDirective implements AfterViewInit {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef);

  ngAfterViewInit(): void {
    queueMicrotask(() => this.el.nativeElement.focus());
  }
}
